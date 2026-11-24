namespace Comet.Game.Social
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgFriend;

    // Friends and enemies. Both lists live on the client from login and in the database;
    // a friendship changes two players at once, so changes run under one gate.
    public static class Relationships
    {
        public const int MaxFriends = 50;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static async Task LoadAsync(Client client)
        {
            var (friends, enemies) = await RelationshipsRepository.LoadAsync(client.ID);
            foreach (var (id, name) in friends)
                client.Friends[id] = name;
            foreach (var (id, name, mesh) in enemies)
                client.Enemies[id] = (name, mesh);
        }

        // Login step 76: the lists, and telling the others this player is online
        public static async Task SendListsAsync(Client client)
        {
            foreach (var (id, name) in client.Friends)
                await client.SendAsync(new MsgFriend(FriendAction.GetInfo, id, name, IsOnline(id)));
            foreach (var (id, enemy) in client.Enemies)
                await client.SendAsync(new MsgFriend(FriendAction.EnemyAdd, id, enemy.Name, IsOnline(id)));
            await AnnounceAsync(client, online: true);
        }

        public static async Task SendEnemyListAsync(Client client)
        {
            if (client.Character == null)
                return;
            var entries = client.Enemies
                .Select(e =>
                {
                    bool online = Kernel.Players.TryGet(e.Key, out var target) && target.Character != null;
                    string name = online ? target.Character.Name : e.Value.Name;
                    uint mesh = online ? target.Character.Mesh : e.Value.Mesh;
                    uint nobility = (uint)Nobility.Peerage.RankOf(e.Key);
                    int gender = Items.Requirements.Gender(mesh);
                    return new MsgEnemyList.Entry(e.Key, name, online, nobility, gender);
                })
                .OrderByDescending(e => e.Online)
                .ThenBy(e => e.ID)
                .Take(MsgEnemyList.MaxEntries);
            await client.SendAsync(new MsgEnemyList(client.ID, entries));
        }

        public static Task LogoutAsync(Client client) => AnnounceAsync(client, online: false);

        public static async Task HandleAsync(Client client, MsgFriend msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case FriendAction.Apply: await ApplyAsync(client, msg.ID); break;
                    case FriendAction.Accept: await AcceptAsync(client, msg.ID); break;
                    case FriendAction.Break: await BreakAsync(client, msg.ID); break;
                    case FriendAction.EnemyDelete: await DeleteEnemyAsync(client, msg.ID); break;
                    default:
                        LogEvents.For(typeof(Relationships), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                            msg.Type, msg.Action, msg.Length);
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        private static async Task ApplyAsync(Client client, uint targetID)
        {
            Kernel.Players.TryGet(targetID, out var target);
            string reason =
                target?.Character == null || target == client ? "offline" :
                client.Friends.ContainsKey(targetID) ? "already_friends" :
                client.Friends.Count >= MaxFriends ? "list_full" :
                target.Friends.Count >= MaxFriends ? "their_list_full" :
                null;
            if (reason != null)
            {
                Reject(client, "apply", targetID, reason);
                string notice = reason switch
                {
                    "already_friends" => $"{target.Character.Name} is already your friend.",
                    "list_full" => "Your friend list is full.",
                    "their_list_full" => $"{target.Character.Name}'s friend list is full.",
                    _ => null,
                };
                if (notice != null)
                    await Talk.NoticeAsync(client, notice);
                return;
            }

            // Asking someone who asked first makes the friendship
            if (target.FriendRequest == client.ID)
            {
                await BefriendAsync(target, client);
                return;
            }

            client.FriendRequest = target.ID;
            await target.SendAsync(new MsgRelation(client.Character, target.ID));
            await target.SendAsync(new MsgFriend(FriendAction.Apply, client.ID, client.Character.Name, true));
            await Talk.NoticeAsync(target, $"{client.Character.Name} wants to be your friend.");
            await Talk.NoticeAsync(client, "Friend request sent.");
        }

        private static async Task AcceptAsync(Client client, uint askerID)
        {
            if (!Kernel.Players.TryGet(askerID, out var asker) || asker.FriendRequest != client.ID ||
                client.Friends.ContainsKey(askerID))
            {
                Reject(client, "accept", askerID, "not_asked");
                return;
            }
            await BefriendAsync(asker, client);
        }

        private static async Task BefriendAsync(Client asker, Client accepter)
        {
            asker.FriendRequest = 0;
            if (accepter.FriendRequest == asker.ID)
                accepter.FriendRequest = 0;
            await RelationshipsRepository.AddFriendsAsync(asker.ID, accepter.ID);
            asker.Friends[accepter.ID] = accepter.Character.Name;
            accepter.Friends[asker.ID] = asker.Character.Name;

            await asker.SendAsync(new MsgFriend(FriendAction.GetInfo, accepter.ID, accepter.Character.Name, true));
            await accepter.SendAsync(new MsgFriend(FriendAction.GetInfo, asker.ID, asker.Character.Name, true));
            var text = $"{asker.Character.Name} and {accepter.Character.Name} are friends from now on!";
            await Talk.NoticeAsync(asker, text);
            await Talk.NoticeAsync(accepter, text);
            LogEvents.For(typeof(Relationships), LogEvents.FriendAdded).Information(
                "{Name} and {Friend} became friends (characters {CharacterId} and {FriendId})",
                asker.Character.Name, accepter.Character.Name, asker.ID, accepter.ID);
        }

        private static async Task BreakAsync(Client client, uint friendID)
        {
            if (!client.Friends.TryGetValue(friendID, out var name))
            {
                Reject(client, "break", friendID, "not_friends");
                return;
            }

            await RelationshipsRepository.RemoveFriendsAsync(client.ID, friendID);
            client.Friends.TryRemove(friendID, out _);
            await client.SendAsync(new MsgFriend(FriendAction.Break, friendID, name, false));
            if (Kernel.Players.TryGet(friendID, out var friend) && friend.Character != null)
            {
                friend.Friends.TryRemove(client.ID, out _);
                await friend.SendAsync(new MsgFriend(FriendAction.Break, client.ID, client.Character.Name, true));
                await Talk.NoticeAsync(friend, $"{client.Character.Name} ended your friendship.");
            }
            LogEvents.For(typeof(Relationships), LogEvents.FriendRemoved).Information(
                "{Name} ended the friendship with {Friend} (characters {CharacterId} and {FriendId})",
                client.Character.Name, name, client.ID, friendID);
        }

        private static async Task DeleteEnemyAsync(Client client, uint enemyID)
        {
            if (!client.Enemies.TryGetValue(enemyID, out var enemy))
            {
                Reject(client, "delete_enemy", enemyID, "not_enemy");
                return;
            }

            await RelationshipsRepository.RemoveEnemyAsync(client.ID, enemyID);
            client.Enemies.TryRemove(enemyID, out _);
            await client.SendAsync(new MsgFriend(FriendAction.EnemyDelete, enemyID, enemy.Name, false));
            LogEvents.For(typeof(Relationships), LogEvents.EnemyRemoved).Information(
                "{Name} removed {Enemy} from their enemies (characters {CharacterId} and {EnemyId})",
                client.Character.Name, enemy.Name, client.ID, enemyID);
        }

        // Friends see the player come and go, and so do players who have them as an enemy
        private static async Task AnnounceAsync(Client client, bool online)
        {
            var character = client.Character;
            if (character == null)
                return;
            var friendAction = online ? FriendAction.Online : FriendAction.Offline;
            foreach (var id in client.Friends.Keys)
                if (Kernel.Players.TryGet(id, out var friend) && friend != client && friend.Character != null)
                    await friend.SendAsync(new MsgFriend(friendAction, client.ID, character.Name, online));

            var enemyAction = online ? FriendAction.EnemyOnline : FriendAction.EnemyOffline;
            foreach (var other in Kernel.Players.Values.Where(p => p != client && p.Enemies.ContainsKey(client.ID)))
                await other.SendAsync(new MsgFriend(enemyAction, client.ID, character.Name, online));
        }

        private static bool IsOnline(uint id) =>
            Kernel.Players.TryGet(id, out var player) && player.Character != null;

        private static void Reject(Client client, string kind, uint id, string reason) =>
            LogEvents.For(typeof(Relationships), LogEvents.FriendRejected).Warning(
                "{Name} can't {Kind} {Id}: {Reason}", client.Character?.Name, kind, id, reason);
    }
}

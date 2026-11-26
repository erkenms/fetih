namespace Fetih.Game.Chat
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgTalk;

    public static class Talk
    {
        public const int WorldMinimumLevel = 70;
        public static readonly TimeSpan WorldCooldown = TimeSpan.FromSeconds(15);

        public static async Task ReceiveAsync(Client client, MsgTalk msg)
        {
            var character = client.Character;
            if (character == null || client.Map == null || string.IsNullOrWhiteSpace(msg.Message))
                return;

            if (Commands.IsCommand(client, msg.Message))
            {
                await Commands.RunAsync(client, msg.Message);
                return;
            }

            if (IsMuted(character, DateTime.UtcNow))
            {
                await RejectAsync(client, msg, "muted",
                    $"You are muted until {character.MutedUntil:yyyy-MM-dd HH:mm} UTC.");
                return;
            }

            // The client fills in the sender itself; never trust it
            msg.SenderName = character.Name;
            msg.SenderMesh = Mesh(character);

            switch (msg.Channel)
            {
                case TalkChannel.Talk:
                    Log(client, msg, null);
                    await Movement.SendToViewAsync(client, msg);
                    break;

                case TalkChannel.Ghost:
                    if (!client.Life.Dead)
                    {
                        await RejectAsync(client, msg, "alive", null);
                        return;
                    }
                    Log(client, msg, null);
                    await Movement.SendToViewAsync(client, msg, CanHearGhost);
                    break;

                case TalkChannel.Whisper:
                    if (!Kernel.Players.TryGet(msg.RecipientName ?? string.Empty, out var target) ||
                        target.Character == null)
                    {
                        await RejectAsync(client, msg, "offline", $"{msg.RecipientName} is not online.");
                        return;
                    }
                    msg.RecipientName = target.Character.Name;
                    msg.RecipientMesh = Mesh(target.Character);
                    Log(client, msg, target.Character.Name);
                    await target.SendAsync(msg);
                    break;

                case TalkChannel.Team:
                    var members = Social.Teams.OthersOf(client);
                    if (members.Count == 0)
                    {
                        await RejectAsync(client, msg, "no_team", "You are not in a team.");
                        return;
                    }
                    Log(client, msg, null);
                    foreach (var member in members)
                        await member.SendAsync(msg);
                    break;

                case TalkChannel.Guild:
                    if (Social.Guilds.Of(client.ID) == null)
                    {
                        await RejectAsync(client, msg, "no_guild", "You are not in a guild.");
                        return;
                    }
                    Log(client, msg, null);
                    foreach (var member in Social.Guilds.OthersOf(client))
                        await member.SendAsync(msg);
                    break;

                case TalkChannel.Clan:
                    if (Social.Clans.Of(client.ID) == null)
                    {
                        await RejectAsync(client, msg, "no_clan", "You are not in a clan.");
                        return;
                    }
                    Log(client, msg, null);
                    foreach (var member in Social.Clans.OthersOf(client))
                        await member.SendAsync(msg);
                    break;

                case TalkChannel.Friend:
                    Log(client, msg, null);
                    foreach (var id in client.Friends.Keys)
                        if (Kernel.Players.TryGet(id, out var friend) && friend.Character != null)
                            await friend.SendAsync(msg);
                    break;

                case TalkChannel.World:
                    // The client stops this itself and tells the player
                    if (character.Level < WorldMinimumLevel)
                    {
                        await RejectAsync(client, msg, "level", null);
                        return;
                    }
                    var wait = client.LastWorldTalk + WorldCooldown - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await RejectAsync(client, msg, "cooldown",
                            $"You can speak in the world channel again in {Math.Ceiling(wait.TotalSeconds)} seconds.");
                        return;
                    }
                    client.LastWorldTalk = DateTime.UtcNow;
                    Log(client, msg, null);
                    foreach (var other in Kernel.Players.Values)
                        if (other != client)
                            await other.SendAsync(msg);
                    break;

                case TalkChannel.Vendor:
                    Log(client, msg, null);
                    await Items.Vending.HawkAsync(client, msg);
                    break;

                case TalkChannel.TradeBoard:
                case TalkChannel.FriendBoard:
                case TalkChannel.TeamBoard:
                case TalkChannel.GuildBoard:
                case TalkChannel.OthersBoard:
                    await MessageBoard.PostAsync(client, msg);
                    break;

                default:
                    LogEvents.For(typeof(Talk), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} channel {Channel} ({Length} bytes)",
                        msg.Type, msg.Channel, msg.Length);
                    await NoticeAsync(client, $"Missing packet {msg.Type}, Channel {msg.Channel}");
                    break;
            }
        }

        public static bool IsMuted(Character character, DateTime now) =>
            character.MutedUntil is DateTime until && until > now;

        public static bool CanHearGhost(bool dead, byte currentClass) =>
            dead || Magic.Learning.ElementOf(currentClass) == Magic.Learning.Element.Water;

        private static bool CanHearGhost(Client client) =>
            client.Character != null && CanHearGhost(client.Life.Dead, client.Character.CurrentClass);

        public static Task NoticeAsync(Client client, string text) =>
            client.SendAsync(new MsgTalk(client.ID, TalkChannel.Service, text));

        public static async Task BroadcastAsync(TalkChannel channel, string text)
        {
            var message = new MsgTalk(0, channel, text);
            foreach (var player in Kernel.Players.Values.ToList())
                if (player.Character != null)
                    await player.SendAsync(message);
        }

        public static Task BroadcastAsync(string text) => BroadcastAsync(TalkChannel.Center, text);

        private static uint Mesh(Character character) =>
            (uint)(character.Mesh + character.Avatar * 10000);

        private static void Log(Client client, MsgTalk msg, string recipient) =>
            LogEvents.For(typeof(Talk), LogEvents.ChatMessage).Information(
                "{Name} said on {Channel} to {Recipient}: {Text}",
                client.Character.Name, msg.Channel.ToString(), recipient, msg.Message);

        private static Task RejectAsync(Client client, MsgTalk msg, string reason, string notice)
        {
            LogEvents.For(typeof(Talk), LogEvents.ChatRejected).Information(
                "{Name} can't speak on {Channel} to {Recipient}: {Reason}",
                client.Character.Name, msg.Channel.ToString(), msg.RecipientName, reason);
            return notice == null ? Task.CompletedTask : NoticeAsync(client, notice);
        }
    }
}

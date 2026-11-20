namespace Comet.Game.Social
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgFamily;

    // Clans stay in memory from startup and every change is written through to the
    // database. Changes come from several players' packets, so they run one at a time
    // under the gate, packets to other players included.
    public static class Clans
    {
        public const byte CreateLevel = 50;
        public const uint CreateCost = 500_000;
        public const uint CreateFund = 250_000;
        public const byte TransferLevel = 50;
        public const int MaxAnnouncement = 127;

        public sealed record Membership(Clan Clan, ClanMember Member);

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentDictionary<uint, Clan> ById = new ConcurrentDictionary<uint, Clan>();
        private static readonly ConcurrentDictionary<uint, Membership> ByMember =
            new ConcurrentDictionary<uint, Membership>();

        // Clan asking for an alliance → the clan asked
        private static readonly Dictionary<uint, uint> AllyRequests = new Dictionary<uint, uint>();

        public static int Count => ById.Count;

        public static Membership Of(uint characterID) =>
            ByMember.TryGetValue(characterID, out var membership) ? membership : null;

        public static async Task LoadAsync()
        {
            var (clans, members, relations) = await ClansRepository.LoadAsync();
            foreach (var row in clans)
                ById[row.FamilyID] = Clan.FromRow(row);
            foreach (var row in members)
            {
                if (!ById.TryGetValue(row.Member.FamilyID, out var clan))
                    continue;
                var member = new ClanMember(row.Member.CharacterID, row.Name, row.Level,
                    (ClanRank)row.Member.Rank, row.Member.Joined)
                {
                    Class = row.Class,
                    Mesh = row.Mesh,
                    Donation = row.Member.Donation,
                };
                clan.Members[member.CharacterID] = member;
                ByMember[member.CharacterID] = new Membership(clan, member);
            }
            foreach (var row in relations)
            {
                if (!ById.TryGetValue(row.FamilyID, out var clan) || !ById.ContainsKey(row.TargetID))
                    continue;
                (row.Kind == Guild.RelationAlly ? clan.Allies : clan.Enemies).Add(row.TargetID);
            }
        }

        public static async Task HandleAsync(Client client, MsgFamily msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case FamilyAction.Query: await SendInfoAsync(client); break;
                    case FamilyAction.QueryMembers: await SendMembersAsync(client); break;
                    case FamilyAction.QueryOccupy: await SendOccupyAsync(client); break;
                    case FamilyAction.Recruit: await RecruitAsync(client, msg.Data); break;
                    case FamilyAction.AcceptRecruit: await AcceptRecruitAsync(client, msg.Data); break;
                    case FamilyAction.Join: await ApplyAsync(client, msg.Data); break;
                    case FamilyAction.AcceptJoin: await AcceptJoinAsync(client, msg.Data); break;
                    case FamilyAction.EnemyList:
                    case FamilyAction.AllyList: await SendRelationsAsync(client); break;
                    case FamilyAction.AddEnemy: await AddEnemyAsync(client, msg.Text); break;
                    case FamilyAction.DeleteEnemy: await DeleteEnemyAsync(client, msg.Text); break;
                    case FamilyAction.AddAlly: await AddAllyAsync(client, msg.Data); break;
                    case FamilyAction.AcceptAlly: await AcceptAllyAsync(client, msg); break;
                    case FamilyAction.DeleteAlly: await DeleteAllyAsync(client, msg.Text); break;
                    case FamilyAction.Abdicate: await AbdicateAsync(client, FindMember(client, msg)); break;
                    case FamilyAction.Kick: await KickAsync(client, FindMember(client, msg)); break;
                    case FamilyAction.Quit: await QuitAsync(client); break;
                    case FamilyAction.Announce: await SendAnnouncementAsync(client); break;
                    case FamilyAction.SetAnnouncement: await AnnounceAsync(client, msg.Text); break;
                    case FamilyAction.Donate: await DonateAsync(client, msg.Data); break;
                    default:
                        LogEvents.For(typeof(Clans), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                            msg.Type, msg.Action, msg.Length, Convert.ToHexString(msg.Encode()));
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        // With the guild at login step 97: the clan window and its allies and enemies
        public static async Task LoginAsync(Client client)
        {
            await Gate.WaitAsync();
            try
            {
                if (Of(client.ID)?.Member is not ClanMember member)
                    return;
                Refresh(member, client);
                await SendInfoAsync(client);
                await SendRelationsAsync(client);
            }
            finally { Gate.Release(); }
        }

        // Online members other than the player, for clan chat
        public static List<Client> OthersOf(Client client)
        {
            var clan = Of(client.ID)?.Clan;
            if (clan == null)
                return new List<Client>();
            return ByMember.Values.Where(m => m.Clan == clan && m.Member.CharacterID != client.ID)
                .Select(m => Online(m.Member.CharacterID)).Where(c => c != null).ToList();
        }

        public static async Task<string> CreateAsync(Client client, string name)
        {
            await Gate.WaitAsync();
            try
            {
                var character = client.Character;
                name = name?.Trim() ?? string.Empty;
                string reason =
                    Of(client.ID) != null ? "in_clan" :
                    character.Level < CreateLevel ? "level" :
                    Guilds.CheckName(name) ?? (ById.Values.Any(c => Named(c, name)) ? "name_taken" : null) ??
                    (character.Silver < CreateCost ? "silver" : null);
                if (reason != null)
                {
                    Reject(client, "create", name, reason);
                    return reason;
                }
                if (!await Progress.ChangeSilverAsync(client, -CreateCost, "clan_create"))
                {
                    Reject(client, "create", name, "silver");
                    return "silver";
                }

                var leader = new ClanMember(client.ID, character.Name, character.Level, ClanRank.Leader, Guild.Today);
                Refresh(leader, client);
                var row = new Clan(0, name) { LeaderID = client.ID, Fund = CreateFund, Created = Guild.Today }.ToRow();
                uint id;
                try { id = await ClansRepository.CreateAsync(row, leader.ToRow(0)); }
                catch (Exception e)
                {
                    await Progress.ChangeSilverAsync(client, CreateCost, "clan_create_refund");
                    LogEvents.For(typeof(Clans), LogEvents.ClanRejected).Error(e,
                        "{Name} can't create clan {Clan}: {Reason}", character.Name, name, "database");
                    return "name_taken";
                }

                var clan = new Clan(id, name) { LeaderID = client.ID, Fund = CreateFund, Created = row.Created };
                clan.Members[client.ID] = leader;
                ById[id] = clan;
                ByMember[client.ID] = new Membership(clan, leader);
                await RefreshAsync(client);
                LogEvents.For(typeof(Clans), LogEvents.ClanCreated).Information(
                    "{Name} created clan {Clan} (clan {ClanId}, fund {Fund})", character.Name, name, id, CreateFund);
                return null;
            }
            finally { Gate.Release(); }
        }

        public static async Task<string> DisbandAsync(Client client)
        {
            await Gate.WaitAsync();
            try
            {
                var membership = Of(client.ID);
                if (membership?.Member.Rank != ClanRank.Leader)
                {
                    Reject(client, "disband", membership?.Clan.Name, "not_leader");
                    return "not_leader";
                }
                var clan = membership.Clan;
                await ClansRepository.DeleteAsync(clan.ID);
                ById.TryRemove(clan.ID, out _);
                foreach (var other in ById.Values)
                {
                    other.Allies.Remove(clan.ID);
                    other.Enemies.Remove(clan.ID);
                }
                foreach (var id in AllyRequests.Where(r => r.Key == clan.ID || r.Value == clan.ID)
                    .Select(r => r.Key).ToList())
                    AllyRequests.Remove(id);
                foreach (var member in clan.Members.Values.ToList())
                {
                    ByMember.TryRemove(member.CharacterID, out _);
                    if (Online(member.CharacterID) is Client player)
                    {
                        await LeftAsync(player, clan);
                        await Talk.NoticeAsync(player, $"The clan {clan.Name} has been disbanded.");
                    }
                }
                LogEvents.For(typeof(Clans), LogEvents.ClanDisbanded).Information(
                    "{Name} disbanded clan {Clan} (clan {ClanId}, {MemberCount} members)",
                    client.Character.Name, clan.Name, clan.ID, clan.Members.Count);
                return null;
            }
            finally { Gate.Release(); }
        }

        private static async Task RecruitAsync(Client client, uint targetID)
        {
            var clan = LedBy(client);
            Kernel.Players.TryGet(targetID, out var target);
            string reason =
                clan == null ? "not_leader" :
                clan.Full ? "full" :
                target?.Character == null || target == client ? "offline" :
                Of(target.ID) != null ? "in_clan" :
                null;
            if (reason != null)
            {
                Reject(client, "recruit", targetID.ToString(CultureInfo.InvariantCulture), reason);
                if (reason == "full")
                    await Talk.NoticeAsync(client, "Your clan is full.");
                else if (reason == "in_clan")
                    await Talk.NoticeAsync(client, $"{target.Character.Name} is already in a clan.");
                return;
            }

            if (target.ClanApply == client.ID)
            {
                target.ClanApply = 0;
                await JoinAsync(target, clan, client);
                return;
            }
            client.ClanInvite = target.ID;
            await target.SendAsync(new MsgRelation(client.Character, target.ID));
            await target.SendAsync(new MsgFamily(FamilyAction.Recruit, client.ID, clan.Name, client.Character.Name));
        }

        // The invited player says yes; the reply's ID may be the leader's or the clan's
        private static async Task AcceptRecruitAsync(Client client, uint id)
        {
            var leader = Kernel.Players.Values.FirstOrDefault(p =>
                p.ClanInvite == client.ID && (p.ID == id || Of(p.ID)?.Clan.ID == id)) ??
                Kernel.Players.Values.FirstOrDefault(p => p.ClanInvite == client.ID);
            var clan = leader == null ? null : LedBy(leader);
            string reason =
                clan == null ? "not_asked" :
                Of(client.ID) != null ? "in_clan" :
                clan.Full ? "full" :
                null;
            if (reason != null)
            {
                Reject(client, "accept_recruit", id.ToString(CultureInfo.InvariantCulture), reason);
                return;
            }
            leader.ClanInvite = 0;
            await JoinAsync(client, clan, leader);
        }

        private static async Task ApplyAsync(Client client, uint leaderID)
        {
            Kernel.Players.TryGet(leaderID, out var leader);
            var clan = leader == null ? null : LedBy(leader);
            string reason =
                Of(client.ID) != null ? "in_clan" :
                clan == null ? "not_leader" :
                clan.Full ? "full" :
                null;
            if (reason != null)
            {
                Reject(client, "apply", leaderID.ToString(CultureInfo.InvariantCulture), reason);
                if (reason == "not_leader")
                    await Talk.NoticeAsync(client, "Find a clan leader to join a clan.");
                else if (reason == "full")
                    await Talk.NoticeAsync(client, "That clan is full.");
                return;
            }

            if (leader.ClanInvite == client.ID)
            {
                leader.ClanInvite = 0;
                await JoinAsync(client, clan, leader);
                return;
            }
            client.ClanApply = leader.ID;
            await leader.SendAsync(new MsgRelation(client.Character, leader.ID));
            await leader.SendAsync(new MsgFamily(FamilyAction.Join, client.ID, client.Character.Name));
        }

        private static async Task AcceptJoinAsync(Client client, uint applicantID)
        {
            var clan = LedBy(client);
            var applicant = Kernel.Players.TryGet(applicantID, out var asked) && asked.ClanApply == client.ID
                ? asked
                : Kernel.Players.Values.FirstOrDefault(p => p.ClanApply == client.ID);
            string reason =
                clan == null ? "not_leader" :
                applicant?.Character == null ? "not_asked" :
                Of(applicant.ID) != null ? "in_clan" :
                clan.Full ? "full" :
                null;
            if (reason != null)
            {
                Reject(client, "accept_join", applicantID.ToString(CultureInfo.InvariantCulture), reason);
                return;
            }
            applicant.ClanApply = 0;
            await JoinAsync(applicant, clan, client);
        }

        private static async Task JoinAsync(Client client, Clan clan, Client recruiter)
        {
            var character = client.Character;
            var member = new ClanMember(client.ID, character.Name, character.Level, ClanRank.Member, Guild.Today);
            Refresh(member, client);
            await ClansRepository.SaveMemberAsync(member.ToRow(clan.ID));
            clan.Members[client.ID] = member;
            ByMember[client.ID] = new Membership(clan, member);
            await RefreshAsync(client);
            await SendRelationsAsync(client);
            await NotifyAsync(clan, $"{character.Name} has joined the clan.");
            LogEvents.For(typeof(Clans), LogEvents.ClanJoined).Information(
                "{Name} joined clan {Clan} (clan {ClanId}, recruited by {Recruiter})",
                character.Name, clan.Name, clan.ID, recruiter.Character.Name);
        }

        private static async Task QuitAsync(Client client)
        {
            var membership = Of(client.ID);
            if (membership == null || membership.Member.Rank == ClanRank.Leader)
            {
                Reject(client, "quit", membership?.Clan.Name, membership == null ? "no_clan" : "leader");
                if (membership != null)
                    await Talk.NoticeAsync(client, "The clan leader can't leave the clan.");
                return;
            }
            await RemoveAsync(membership.Clan, membership.Member, "left", null);
        }

        private static async Task KickAsync(Client client, ClanMember target)
        {
            var clan = LedBy(client);
            string reason =
                clan == null ? "not_leader" :
                target == null || !clan.Members.ContainsKey(target.CharacterID) ? "not_member" :
                target.CharacterID == client.ID ? "self" :
                null;
            if (reason != null)
            {
                Reject(client, "kick", target?.Name, reason);
                return;
            }
            await RemoveAsync(clan, target, "kicked", client.Character.Name);
        }

        private static async Task RemoveAsync(Clan clan, ClanMember member, string reason, string by)
        {
            await ClansRepository.RemoveMemberAsync(member.CharacterID);
            clan.Members.Remove(member.CharacterID);
            ByMember.TryRemove(member.CharacterID, out _);
            if (Online(member.CharacterID) is Client player)
            {
                await LeftAsync(player, clan);
                if (reason == "kicked")
                    await Talk.NoticeAsync(player, $"You have been expelled from the clan {clan.Name}.");
            }
            await NotifyAsync(clan, reason == "kicked"
                ? $"{member.Name} has been expelled from the clan by {by}."
                : $"{member.Name} has left the clan.");
            LogEvents.For(typeof(Clans), LogEvents.ClanLeft).Information(
                "{Name} left clan {Clan} (clan {ClanId}, {Reason}, by {By})",
                member.Name, clan.Name, clan.ID, reason, by);
        }

        // The leader hands the clan to an online member of level 50 or more
        private static async Task AbdicateAsync(Client client, ClanMember target)
        {
            var clan = LedBy(client);
            var heir = target == null ? null : Online(target.CharacterID);
            string reason =
                clan == null ? "not_leader" :
                target == null || !clan.Members.ContainsKey(target.CharacterID) ? "not_member" :
                target.CharacterID == client.ID ? "self" :
                heir == null ? "offline" :
                heir.Character.Level < TransferLevel ? "level" :
                null;
            if (reason != null)
            {
                Reject(client, "abdicate", target?.Name, reason);
                if (reason == "offline")
                    await Talk.NoticeAsync(client, "You can only pass the clan to a member who is online.");
                else if (reason == "level")
                    await Talk.NoticeAsync(client, $"You can only pass the clan to a member of level {TransferLevel} or more.");
                return;
            }

            var old = clan.Leader;
            old.Rank = ClanRank.Member;
            target.Rank = ClanRank.Leader;
            clan.LeaderID = target.CharacterID;
            await ClansRepository.SaveMemberAsync(old.ToRow(clan.ID));
            await ClansRepository.SaveMemberAsync(target.ToRow(clan.ID));
            await ClansRepository.SaveAsync(clan.ToRow());
            await RefreshAsync(client);
            await RefreshAsync(heir);
            await NotifyAsync(clan, $"{client.Character.Name} has passed the leadership of the clan to {target.Name}.");
            LogEvents.For(typeof(Clans), LogEvents.ClanLeader).Information(
                "{Name} passed clan {Clan} to {Leader} (clan {ClanId})",
                client.Character.Name, clan.Name, target.Name, clan.ID);
        }

        // Asking a clan whose leader asked first makes the alliance
        private static async Task AddAllyAsync(Client client, uint targetLeaderID)
        {
            var clan = LedBy(client);
            Kernel.Players.TryGet(targetLeaderID, out var targetLeader);
            var target = targetLeader == null ? null : LedBy(targetLeader);
            string reason =
                clan == null ? "not_leader" :
                target == null ? "target_not_leader" :
                target == clan ? "own_clan" :
                clan.Allies.Contains(target.ID) ? "already_allied" :
                clan.Enemies.Contains(target.ID) ? "enemy" :
                clan.Allies.Count >= Clan.MaxAllies ? "allies_full" :
                target.Allies.Count >= Clan.MaxAllies ? "their_allies_full" :
                null;
            if (reason != null)
            {
                Reject(client, "ally", target?.Name, reason);
                string notice = reason switch
                {
                    "target_not_leader" => "The target is not a clan leader.",
                    "enemy" => $"You must remove the clan {target.Name} from your enemies first.",
                    "allies_full" or "their_allies_full" => $"A clan can have {Clan.MaxAllies} allied clans at most.",
                    _ => null,
                };
                if (notice != null)
                    await Talk.NoticeAsync(client, notice);
                return;
            }

            if (AllyRequests.TryGetValue(target.ID, out var asked) && asked == clan.ID)
            {
                await AllyAsync(client, clan, target);
                return;
            }
            AllyRequests[clan.ID] = target.ID;
            await targetLeader.SendAsync(new MsgFamily(FamilyAction.AddAlly, clan.ID, clan.Name, client.Character.Name));
        }

        // The asked leader says yes, naming the asking leader, or their clan by ID
        private static async Task AcceptAllyAsync(Client client, MsgFamily msg)
        {
            var clan = LedBy(client);
            var asking = AllyRequests.Where(r => clan != null && r.Value == clan.ID)
                .Select(r => ById.GetValueOrDefault(r.Key)).Where(c => c != null)
                .OrderByDescending(c => c.ID == msg.Data || c.LeaderID == msg.Data ||
                    string.Equals(c.Leader?.Name, msg.Text, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            string reason =
                clan == null ? "not_leader" :
                asking == null ? "not_asked" :
                clan.Allies.Count >= Clan.MaxAllies || asking.Allies.Count >= Clan.MaxAllies ? "allies_full" :
                null;
            if (reason != null)
            {
                Reject(client, "accept_ally", msg.Text, reason);
                return;
            }
            await AllyAsync(client, asking, clan);
        }

        private static async Task AllyAsync(Client client, Clan asking, Clan asked)
        {
            AllyRequests.Remove(asking.ID);
            asking.Allies.Add(asked.ID);
            asked.Allies.Add(asking.ID);
            await ClansRepository.SetRelationAsync(asking.ID, asked.ID, Guild.RelationAlly);
            await ClansRepository.SetRelationAsync(asked.ID, asking.ID, Guild.RelationAlly);
            await BroadcastRelationsAsync(asking);
            await BroadcastRelationsAsync(asked);
            await NotifyAsync(asking, $"The clan {asked.Name} is now an allied clan.");
            await NotifyAsync(asked, $"The clan {asking.Name} is now an allied clan.");
            LogRelation(client, asking, asked, "ally");
        }

        private static async Task DeleteAllyAsync(Client client, string name)
        {
            var clan = LedBy(client);
            var target = ById.Values.FirstOrDefault(c => Named(c, name));
            if (clan == null || target == null || !clan.Allies.Contains(target.ID))
            {
                Reject(client, "unally", name, clan == null ? "not_leader" : "not_allied");
                return;
            }
            clan.Allies.Remove(target.ID);
            target.Allies.Remove(clan.ID);
            await ClansRepository.RemoveRelationAsync(clan.ID, target.ID);
            await ClansRepository.RemoveRelationAsync(target.ID, clan.ID);
            await BroadcastRelationsAsync(clan);
            await BroadcastRelationsAsync(target);
            await NotifyAsync(target, $"The clan {clan.Name} has ended our alliance.");
            LogRelation(client, clan, target, "unally");
        }

        private static async Task AddEnemyAsync(Client client, string name)
        {
            var clan = LedBy(client);
            var target = ById.Values.FirstOrDefault(c => Named(c, name));
            string reason =
                clan == null ? "not_leader" :
                target == null || target == clan ? "no_clan" :
                clan.Enemies.Contains(target.ID) ? "already_enemy" :
                clan.Allies.Contains(target.ID) ? "ally" :
                clan.Enemies.Count >= Clan.MaxEnemies ? "enemies_full" :
                null;
            if (reason != null)
            {
                Reject(client, "enemy", name, reason);
                string notice = reason switch
                {
                    "no_clan" => $"There is no clan named {name}.",
                    "already_enemy" => $"The clan {target.Name} is already an enemy clan.",
                    "ally" => $"You must end the alliance with the clan {target.Name} first.",
                    "enemies_full" => $"There are already {Clan.MaxEnemies} enemy clans.",
                    _ => null,
                };
                if (notice != null)
                    await Talk.NoticeAsync(client, notice);
                return;
            }
            clan.Enemies.Add(target.ID);
            await ClansRepository.SetRelationAsync(clan.ID, target.ID, Guild.RelationEnemy);
            await BroadcastRelationsAsync(clan);
            await NotifyAsync(target, $"The clan {clan.Name} has made us an enemy clan.");
            LogRelation(client, clan, target, "enemy");
        }

        private static async Task DeleteEnemyAsync(Client client, string name)
        {
            var clan = LedBy(client);
            var target = ById.Values.FirstOrDefault(c => Named(c, name));
            if (clan == null || target == null || !clan.Enemies.Remove(target.ID))
            {
                Reject(client, "unenemy", name, clan == null ? "not_leader" : "not_enemy");
                return;
            }
            await ClansRepository.RemoveRelationAsync(clan.ID, target.ID);
            await BroadcastRelationsAsync(clan);
            LogRelation(client, clan, target, "unenemy");
        }

        private static async Task DonateAsync(Client client, uint amount)
        {
            var membership = Of(client.ID);
            if (membership == null || amount == 0)
            {
                Reject(client, "donate", membership?.Clan.Name, membership == null ? "no_clan" : "amount");
                return;
            }
            if (!await Progress.ChangeSilverAsync(client, -amount, "clan_donation"))
            {
                Reject(client, "donate", membership.Clan.Name, "silver");
                await Talk.NoticeAsync(client, "You don't have enough silver.");
                return;
            }
            var clan = membership.Clan;
            clan.Fund += amount;
            membership.Member.Donation += amount;
            await ClansRepository.SaveAsync(clan.ToRow());
            await ClansRepository.SaveMemberAsync(membership.Member.ToRow(clan.ID));
            await SendInfoAsync(client);
            LogEvents.For(typeof(Clans), LogEvents.ClanDonated).Information(
                "{Name} donated {Amount} silver to clan {Clan} (clan {ClanId}, fund {Fund})",
                client.Character.Name, amount, clan.Name, clan.ID, clan.Fund);
        }

        private static Task SendAnnouncementAsync(Client client)
        {
            var clan = Of(client.ID)?.Clan;
            if (clan == null)
                return Task.CompletedTask;
            return client.SendAsync(new MsgFamily(FamilyAction.Announce, clan.ID, clan.Announcement));
        }

        private static async Task AnnounceAsync(Client client, string text)
        {
            var clan = LedBy(client);
            if (clan == null)
            {
                Reject(client, "announce", null, "not_leader");
                return;
            }
            clan.Announcement = text.Length > MaxAnnouncement ? text[..MaxAnnouncement] : text;
            await ClansRepository.SaveAsync(clan.ToRow());
            await BroadcastAsync(clan, new MsgFamily(FamilyAction.Announce, clan.ID, clan.Announcement));
            LogEvents.For(typeof(Clans), LogEvents.ClanAnnounced).Information(
                "{Name} set the announcement of clan {Clan} (clan {ClanId}): {Text}",
                client.Character.Name, clan.Name, clan.ID, clan.Announcement);
        }

        private static async Task SendMembersAsync(Client client)
        {
            var clan = Of(client.ID)?.Clan;
            if (clan == null)
                return;
            var members = clan.Members.Values
                .Select(m => (Member: m, Player: Online(m.CharacterID)))
                .OrderByDescending(x => x.Player != null)
                .ThenByDescending(x => x.Member.Rank)
                .ThenBy(x => x.Member.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var (member, player) in members)
                if (player != null)
                    Refresh(member, player);
            await client.SendAsync(new MsgFamily(FamilyAction.QueryMembers, clan.ID)
            {
                Members = members.Select(x => (x.Member, x.Player != null)).ToList(),
            });
        }

        // The clan window: "ID members 0 fund level rank 0 0 0 0 0 own-donation", the name,
        // the leader, then the clan war's map details, unused
        private static Task SendInfoAsync(Client client)
        {
            var membership = Of(client.ID);
            if (membership == null)
                return Task.CompletedTask;
            var clan = membership.Clan;
            var member = membership.Member;
            return client.SendAsync(new MsgFamily(FamilyAction.Query, clan.ID,
                $"{clan.ID} {clan.Members.Count} 0 {clan.Fund} {clan.Level} {(int)member.Rank} 0 0 0 0 0 {member.Donation}",
                clan.Name, clan.Leader?.Name ?? string.Empty, "0 0 0 0 0 0 0", string.Empty, string.Empty));
        }

        private static async Task SendOccupyAsync(Client client)
        {
            var clan = Of(client.ID)?.Clan;
            if (clan == null)
                return;
            await SendInfoAsync(client);
            await client.SendAsync(new MsgFamily(FamilyAction.QueryOccupy, clan.ID, "0 0 0 0 0 0 0 0"));
        }

        private static async Task SendRelationsAsync(Client client)
        {
            var clan = Of(client.ID)?.Clan;
            if (clan == null)
                return;
            await client.SendAsync(new MsgFamily(FamilyAction.AllyList, clan.ID) { Relations = Resolve(clan.Allies) });
            await client.SendAsync(new MsgFamily(FamilyAction.EnemyList, clan.ID) { Relations = Resolve(clan.Enemies) });
        }

        private static List<Clan> Resolve(IEnumerable<uint> ids) =>
            ids.Select(id => ById.GetValueOrDefault(id)).Where(c => c != null).ToList();

        private static async Task BroadcastRelationsAsync(Clan clan)
        {
            foreach (var id in clan.Members.Keys)
                if (Online(id) is Client player)
                    await SendRelationsAsync(player);
        }

        // After joining or a new leader: the clan window, and how others see the player
        private static async Task RefreshAsync(Client client)
        {
            await SendInfoAsync(client);
            await Movement.SendToViewAsync(client, new MsgPlayer(client));
        }

        private static async Task LeftAsync(Client client, Clan clan)
        {
            await client.SendAsync(new MsgFamily(FamilyAction.Quit, clan.ID));
            await Movement.SendToViewAsync(client, new MsgPlayer(client));
        }

        // The member named in the packet, or with the ID in it
        private static ClanMember FindMember(Client client, MsgFamily msg)
        {
            var clan = Of(client.ID)?.Clan;
            if (clan == null)
                return null;
            return (msg.Strings.Count > 0 ? clan.Find(msg.Text) : null) ?? clan.Members.GetValueOrDefault(msg.Data);
        }

        private static void Refresh(ClanMember member, Client client)
        {
            var character = client.Character;
            member.Name = character.Name;
            member.Level = character.Level;
            member.Class = character.CurrentClass;
            member.Mesh = (uint)(character.Mesh + character.Avatar * 10000);
        }

        private static Clan LedBy(Client client) =>
            Of(client.ID) is Membership m && m.Member.Rank == ClanRank.Leader ? m.Clan : null;

        private static bool Named(Clan clan, string name) =>
            string.Equals(clan.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static Client Online(uint characterID) =>
            Kernel.Players.TryGet(characterID, out var player) && player.Character != null ? player : null;

        private static async Task BroadcastAsync(Clan clan, IPacket packet)
        {
            foreach (var id in clan.Members.Keys)
                if (Online(id) is Client player)
                    await player.SendAsync(packet);
        }

        private static async Task NotifyAsync(Clan clan, string text)
        {
            foreach (var id in clan.Members.Keys)
                if (Online(id) is Client player)
                    await player.SendAsync(new MsgTalk(player.ID, MsgTalk.TalkChannel.Clan, text));
        }

        private static void LogRelation(Client client, Clan clan, Clan target, string kind) =>
            LogEvents.For(typeof(Clans), LogEvents.ClanRelation).Information(
                "{Name} set {Kind} between clan {Clan} and {Target} (clans {ClanId} and {TargetId})",
                client.Character.Name, kind, clan.Name, target.Name, clan.ID, target.ID);

        private static void Reject(Client client, string kind, string target, string reason) =>
            LogEvents.For(typeof(Clans), LogEvents.ClanRejected).Warning(
                "{Name} can't {Kind} {Target}: {Reason}", client.Character?.Name, kind, target, reason);
    }
}

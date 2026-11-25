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
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgSyndicate;

    // Guilds stay in memory from startup and every change is written through to the
    // database. Changes come from several players' packets, so they run one at a time
    // under the gate, packets to other players included.
    public static class Guilds
    {
        public const byte CreateLevel = 90;
        public const uint CreateCost = 1_000_000;
        public const uint CreateFund = 500_000;
        public const int MaxAnnouncement = 127;
        public const int MaxNameLength = 15;

        public sealed record Membership(Guild Guild, GuildMember Member);

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentDictionary<uint, Guild> ById = new ConcurrentDictionary<uint, Guild>();
        private static readonly ConcurrentDictionary<uint, Membership> ByMember =
            new ConcurrentDictionary<uint, Membership>();
        private static readonly ConcurrentDictionary<uint, uint> ByInscribedItem =
            new ConcurrentDictionary<uint, uint>();

        // Guild asking for an alliance → the guild asked
        private static readonly Dictionary<uint, uint> AllyRequests = new Dictionary<uint, uint>();

        public static int Count => ById.Count;

        // Work on a guild's funds from outside this class, one change at a time like the rest
        public static async Task<T> ChangeAsync<T>(Func<Task<T>> work)
        {
            await Gate.WaitAsync();
            try { return await work(); }
            finally { Gate.Release(); }
        }

        public static Guild Find(uint guildID) =>
            ById.TryGetValue(guildID, out var guild) ? guild : null;

        public static Membership Of(uint characterID) =>
            ByMember.TryGetValue(characterID, out var membership) ? membership : null;

        public static bool IsInscribed(uint itemID) => ByInscribedItem.ContainsKey(itemID);

        public static uint ArsenalSyndicateOf(uint itemID) =>
            ByInscribedItem.TryGetValue(itemID, out var guildID) ? guildID : 0u;

        public static uint BattlePowerOf(uint characterID) =>
            ByMember.TryGetValue(characterID, out var membership)
                ? ArsenalConfig.SharedBattlePower(membership.Guild.Arsenal.TotalBattlePower, membership.Member.Rank)
                : 0u;

        public static async Task LoadAsync()
        {
            var (guilds, members, relations, poles, totems) = await GuildsRepository.LoadAsync();
            foreach (var row in guilds)
                ById[row.SyndicateID] = Guild.FromRow(row);
            foreach (var row in members)
            {
                if (!ById.TryGetValue(row.Member.SyndicateID, out var guild))
                    continue;
                var member = new GuildMember(row.Member.CharacterID, row.Name, row.Level,
                    (GuildRank)row.Member.Rank, row.Member.Joined)
                {
                    SilverDonation = row.Member.SilverDonation,
                    ConquerPointsDonation = row.Member.ConquerPointsDonation,
                };
                guild.Members[member.CharacterID] = member;
                ByMember[member.CharacterID] = new Membership(guild, member);
            }
            foreach (var row in relations)
            {
                if (!ById.TryGetValue(row.SyndicateID, out var guild) || !ById.ContainsKey(row.TargetID))
                    continue;
                (row.Kind == Guild.RelationAlly ? guild.Allies : guild.Enemies).Add(row.TargetID);
            }
            foreach (var row in poles)
            {
                if (!ById.TryGetValue(row.SyndicateID, out var guild) || row.Type >= ArsenalConfig.PoleCount)
                    continue;
                guild.Arsenal.Unlock((TotemPoleType)row.Type, row.Enhancement, row.EnhancementExpiration);
            }
            foreach (var row in totems)
            {
                if (!ById.TryGetValue(row.Totem.SyndicateID, out var guild) || row.Totem.Type >= ArsenalConfig.PoleCount)
                    continue;
                var poleType = (TotemPoleType)row.Totem.Type;
                var pole = guild.Arsenal.Get(poleType);
                if (pole == null)
                    continue;
                pole.Items[row.Totem.ItemID] = new TotemEntry(
                    row.Totem.ItemID, row.Totem.CharacterID, row.OwnerName, poleType,
                    row.TypeID, row.Plus, row.Gem1, row.Gem2);
                ByInscribedItem[row.Totem.ItemID] = guild.ID;
            }
            uint today = Guild.Today;
            foreach (var guild in ById.Values)
            {
                guild.Arsenal.Recalculate(Kernel.ArsenalConfig, today, guild.Members);
                guild.Level = guild.Arsenal.GuildLevel;
            }
        }

        public static async Task RenameAsync(uint characterID, string name)
        {
            await Gate.WaitAsync();
            try
            {
                if (ByMember.TryGetValue(characterID, out var membership))
                    membership.Member.Name = name;
            }
            finally
            {
                Gate.Release();
            }
        }

        public static async Task HandleAsync(Client client, MsgSyndicate msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case SyndicateAction.ApplyJoin: await ApplyJoinAsync(client, msg.Data); break;
                    case SyndicateAction.InviteJoin: await InviteJoinAsync(client, msg.Data); break;
                    case SyndicateAction.Leave: await LeaveAsync(client); break;
                    case SyndicateAction.QueryName: await SendNameAsync(client, msg.Data); break;
                    case SyndicateAction.Ally: await AllyAsync(client, ById.Values.FirstOrDefault(g => Named(g, msg.Text))); break;
                    case SyndicateAction.AllyRequest:
                        await AllyAsync(client, ById.GetValueOrDefault(msg.Data) ??
                            ById.Values.FirstOrDefault(g => Named(g, msg.Text)));
                        break;
                    case SyndicateAction.Unally: await UnallyAsync(client, msg.Text); break;
                    case SyndicateAction.Enemy: await EnemyAsync(client, msg.Text); break;
                    case SyndicateAction.Unenemy: await UnenemyAsync(client, msg.Text); break;
                    case SyndicateAction.DonateSilver: await DonateAsync(client, msg.Data, conquerPoints: false); break;
                    case SyndicateAction.DonateConquerPoints: await DonateAsync(client, msg.Data, conquerPoints: true); break;
                    case SyndicateAction.QueryAttribute: await SendInfoAsync(client); break;
                    case SyndicateAction.SetRequirements: await SetRequirementsAsync(client, msg); break;
                    case SyndicateAction.Announce: await AnnounceAsync(client, msg.Text); break;
                    case SyndicateAction.Promote: await SetRankAsync(client, msg.Text, GuildRank.DeputyLeader); break;
                    case SyndicateAction.Discharge: await SetRankAsync(client, msg.Text, GuildRank.Member); break;
                    case SyndicateAction.PromotionList: await SendPromotionListAsync(client); break;
                    default:
                        LogEvents.For(typeof(Guilds), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                            msg.Type, msg.Action, msg.Length, Convert.ToHexString(msg.Encode()));
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        // Login step 97: the player's guild, its announcement, allies and enemies
        public static async Task LoginAsync(Client client)
        {
            await Gate.WaitAsync();
            try
            {
                if (Of(client.ID) is Membership membership)
                {
                    membership.Member.Name = client.Character.Name;
                    membership.Member.Level = client.Character.Level;
                    foreach (var pole in membership.Guild.Arsenal.Poles.Values)
                        foreach (var entry in pole.Items.Values)
                            if (entry.CharacterID == client.ID)
                                entry.OwnerName = client.Character.Name;
                }
                await SendInfoAsync(client);
                if (Of(client.ID) is Membership m)
                {
                    uint bp = ArsenalConfig.SharedBattlePower(m.Guild.Arsenal.TotalBattlePower, m.Member.Rank);
                    await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.GuildBattlePower, bp));
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task SendMembersAsync(Client client, uint startIndex)
        {
            await Gate.WaitAsync();
            try
            {
                var guild = Of(client.ID)?.Guild;
                if (guild == null)
                    return;
                var page = new MsgSynMemberList { StartIndex = startIndex };
                foreach (var member in guild.Members.Values
                    .Select(m => (Member: m, Online: Online(m.CharacterID)))
                    .OrderByDescending(x => x.Member.Rank)
                    .ThenByDescending(x => x.Online != null)
                    .ThenBy(x => x.Member.Name, StringComparer.OrdinalIgnoreCase)
                    .Skip((int)startIndex).Take(MsgSynMemberList.PageSize))
                {
                    if (member.Online?.Character != null)
                        member.Member.Level = member.Online.Character.Level;
                    page.Members.Add((member.Member, member.Online != null));
                }
                await client.SendAsync(page);
            }
            finally { Gate.Release(); }
        }

        public static Task SendDonationsAsync(Client client) =>
            client.SendAsync(new MsgSynpOffer(Of(client.ID)?.Member));

        public static async Task SendDonationRankingAsync(
            Client client, MsgFactionRankInfo.FactionRankType rankType, byte pageIndex)
        {
            if ((byte)rankType >= MsgFactionRankInfo.MaxRankTypes)
                return;
            await Gate.WaitAsync();
            try
            {
                var guild = Of(client.ID)?.Guild;
                if (guild == null)
                    return;
                var ranked = guild.Members.Values
                    .OrderByDescending(m => DonationFor(m, rankType))
                    .ThenByDescending(m => m.TotalDonation)
                    .ThenByDescending(m => m.Rank)
                    .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int start = pageIndex * MsgFactionRankInfo.PageSize;
                var reply = new MsgFactionRankInfo
                {
                    RankType = rankType,
                    PageIndex = pageIndex,
                    MaxRankSize = (ushort)Math.Min(ranked.Count, ushort.MaxValue),
                };
                for (int i = 0; i < MsgFactionRankInfo.PageSize && start + i < ranked.Count; i++)
                {
                    var member = ranked[start + i];
                    reply.Entries.Add((member, (uint)(start + i)));
                    if (Flowers.FlowerRanking.Of(member.CharacterID) is { } flowers)
                        reply.Flowers[member.CharacterID] = flowers;
                }
                await client.SendAsync(reply);
            }
            finally { Gate.Release(); }
        }

        private static uint DonationFor(GuildMember member, MsgFactionRankInfo.FactionRankType rankType) => rankType switch
        {
            MsgFactionRankInfo.FactionRankType.Silver => member.SilverDonation,
            MsgFactionRankInfo.FactionRankType.ConquerPoints => member.ConquerPointsDonation,
            MsgFactionRankInfo.FactionRankType.Arsenal => member.ArsenalDonation,
            MsgFactionRankInfo.FactionRankType.Total => member.TotalDonation,
            MsgFactionRankInfo.FactionRankType.Rose => FlowerTotal(member, Flowers.FlowerKind.RedRose),
            MsgFactionRankInfo.FactionRankType.Orchid => FlowerTotal(member, Flowers.FlowerKind.Orchid),
            MsgFactionRankInfo.FactionRankType.Lily => FlowerTotal(member, Flowers.FlowerKind.Lily),
            MsgFactionRankInfo.FactionRankType.Tulip => FlowerTotal(member, Flowers.FlowerKind.Tulip),
            _ => 0u,
        };

        private static uint FlowerTotal(GuildMember member, Flowers.FlowerKind kind) =>
            Flowers.FlowerRanking.Of(member.CharacterID)?.TotalOf(kind) ?? 0;

        public static List<Client> OthersOf(Client client)
        {
            var guild = Of(client.ID)?.Guild;
            if (guild == null)
                return new List<Client>();
            return ByMember.Values.Where(m => m.Guild == guild && m.Member.CharacterID != client.ID)
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
                    Of(client.ID) != null ? "in_guild" :
                    character.Level < CreateLevel ? "level" :
                    CheckName(name) ?? (ById.Values.Any(g => Named(g, name)) ? "name_taken" : null) ??
                    (character.Silver < CreateCost ? "silver" : null);
                if (reason != null)
                {
                    Reject(client, "create", name, reason);
                    return reason;
                }
                if (!await Progress.ChangeSilverAsync(client, -CreateCost, "guild_create"))
                {
                    Reject(client, "create", name, "silver");
                    return "silver";
                }

                var guild = new Guild(0, name) { LeaderID = client.ID, Fund = CreateFund, Created = Guild.Today };
                var leader = new GuildMember(client.ID, character.Name, character.Level, GuildRank.Leader, Guild.Today);
                uint id;
                try { id = await GuildsRepository.CreateAsync(guild.ToRow(), leader.ToRow(0)); }
                catch (Exception e)
                {
                    await Progress.ChangeSilverAsync(client, CreateCost, "guild_create_refund");
                    LogEvents.For(typeof(Guilds), LogEvents.GuildRejected).Error(e,
                        "{Name} can't create guild {Guild}: {Reason}", character.Name, name, "database");
                    return "name_taken";
                }

                guild = new Guild(id, name) { LeaderID = client.ID, Fund = CreateFund, Created = guild.Created };
                guild.Members[client.ID] = leader;
                ById[id] = guild;
                ByMember[client.ID] = new Membership(guild, leader);
                await SendInfoAsync(client);
                await Movement.SendToViewAsync(client, new MsgPlayer(client));
                var text = $"Congratulations! {character.Name} has set up the guild {name}.";
                foreach (var player in Kernel.Players.Values.Where(p => p.Character != null))
                    await player.SendAsync(new MsgTalk(player.ID, MsgTalk.TalkChannel.System, text));
                LogEvents.For(typeof(Guilds), LogEvents.GuildCreated).Information(
                    "{Name} created guild {Guild} (guild {GuildId}, fund {Fund})",
                    character.Name, name, id, CreateFund);
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
                if (membership?.Member.Rank != GuildRank.Leader)
                {
                    Reject(client, "disband", membership?.Guild.Name, "not_leader");
                    return "not_leader";
                }
                await DisbandLockedAsync(membership.Guild, client.Character.Name);
                return null;
            }
            finally { Gate.Release(); }
        }

        // A disband from the admin panel, whether the leader is online or not
        public static async Task<bool> DisbandAsync(uint guildID, string actor)
        {
            await Gate.WaitAsync();
            try
            {
                if (!ById.TryGetValue(guildID, out var guild))
                    return false;
                await DisbandLockedAsync(guild, actor);
                return true;
            }
            finally { Gate.Release(); }
        }

        private static async Task DisbandLockedAsync(Guild guild, string actor)
        {
            var removedByOwner = new Dictionary<uint, List<(TotemPoleType Pole, uint ItemID)>>();
            foreach (var pole in guild.Arsenal.Poles.Values)
            {
                foreach (var entry in pole.Items.Values)
                {
                    ByInscribedItem.TryRemove(entry.ItemID, out _);
                    if (!removedByOwner.TryGetValue(entry.CharacterID, out var list))
                        removedByOwner[entry.CharacterID] = list = new List<(TotemPoleType, uint)>();
                    list.Add((pole.Type, entry.ItemID));
                }
                pole.Items.Clear();
            }
            await GuildsRepository.DeleteAsync(guild.ID);
            ById.TryRemove(guild.ID, out _);
            GuildRewards.Forget(guild.ID);
            foreach (var other in ById.Values)
            {
                other.Allies.Remove(guild.ID);
                other.Enemies.Remove(guild.ID);
            }
            foreach (var id in AllyRequests.Where(r => r.Key == guild.ID || r.Value == guild.ID)
                .Select(r => r.Key).ToList())
                AllyRequests.Remove(id);
            foreach (var member in guild.Members.Values.ToList())
            {
                ByMember.TryRemove(member.CharacterID, out _);
                if (Online(member.CharacterID) is Client player)
                {
                    if (removedByOwner.TryGetValue(member.CharacterID, out var removedItems))
                    {
                        foreach (var (poleType, itemID) in removedItems)
                            await player.SendAsync(new MsgTotemPole
                            {
                                Action = MsgTotemPole.TotemPoleAction.Remove,
                                TotemType = (uint)poleType,
                                ItemID = itemID,
                            });
                    }
                    await player.SendAsync(new MsgUserAttrib(player.ID, MsgUserAttrib.AttributeType.GuildBattlePower, 0));
                    await player.SendAsync(new MsgSyndicate(SyndicateAction.Disband, guild.ID));
                    await RefreshAsync(player);
                    await Talk.NoticeAsync(player, $"The guild {guild.Name} has been disbanded.");
                }
            }
            await GuildWar.OnGuildDisbandedAsync(guild.ID);
            LogEvents.For(typeof(Guilds), LogEvents.GuildDisbanded).Information(
                "{Name} disbanded guild {Guild} (guild {GuildId}, {MemberCount} members)",
                actor, guild.Name, guild.ID, guild.Members.Count);
        }

        public static async Task<string> TransferAsync(Client client, string name)
        {
            await Gate.WaitAsync();
            try
            {
                var membership = Of(client.ID);
                var guild = membership?.Guild;
                var target = guild?.Find(name?.Trim() ?? string.Empty);
                string reason =
                    membership?.Member.Rank != GuildRank.Leader ? "not_leader" :
                    target == null ? "not_member" :
                    target.CharacterID == client.ID ? "self" :
                    null;
                if (reason != null)
                {
                    Reject(client, "transfer", name, reason);
                    return reason;
                }

                membership.Member.Rank = GuildRank.Member;
                target.Rank = GuildRank.Leader;
                guild.LeaderID = target.CharacterID;
                await GuildsRepository.SaveMemberAsync(membership.Member.ToRow(guild.ID));
                await GuildsRepository.SaveMemberAsync(target.ToRow(guild.ID));
                await GuildsRepository.SaveAsync(guild.ToRow());
                await RefreshAsync(client);
                await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.GuildBattlePower,
                    ArsenalConfig.SharedBattlePower(guild.Arsenal.TotalBattlePower, membership.Member.Rank)));
                if (Online(target.CharacterID) is Client player)
                {
                    await RefreshAsync(player);
                    await player.SendAsync(new MsgUserAttrib(player.ID, MsgUserAttrib.AttributeType.GuildBattlePower,
                        ArsenalConfig.SharedBattlePower(guild.Arsenal.TotalBattlePower, target.Rank)));
                }
                await NotifyAsync(guild, $"{client.Character.Name} has passed the leadership of the guild to {target.Name}.");
                LogRank(client, guild, target, "transfer");
                return null;
            }
            finally { Gate.Release(); }
        }

        // From the member list (MsgName GuildKick) with the member's name
        public static async Task KickAsync(Client client, string name)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                var membership = Of(client.ID);
                var target = membership?.Guild.Find(name ?? string.Empty);
                string reason =
                    membership == null || membership.Member.Rank < GuildRank.DeputyLeader ? "not_authorized" :
                    target == null ? "not_member" :
                    target.Rank >= membership.Member.Rank ? "rank" :
                    null;
                if (reason != null)
                {
                    Reject(client, "kick", name, reason);
                    if (reason == "rank")
                        await Talk.NoticeAsync(client, $"You can't expel {target.Name}.");
                    return;
                }
                await RemoveAsync(membership.Guild, target, "kicked", client.Character.Name);
            }
            finally { Gate.Release(); }
        }

        private static async Task ApplyJoinAsync(Client client, uint recruiterID)
        {
            Kernel.Players.TryGet(recruiterID, out var recruiter);
            var membership = recruiter == null ? null : Of(recruiter.ID);
            string reason =
                Of(client.ID) != null ? "in_guild" :
                recruiter?.Character == null || recruiter == client ? "offline" :
                membership == null || membership.Member.Rank < GuildRank.DeputyLeader ? "not_recruiter" :
                CheckRequirements(membership.Guild, client);
            if (reason != null)
            {
                Reject(client, "apply", recruiterID.ToString(CultureInfo.InvariantCulture), reason);
                await NoticeRejectAsync(client, reason, membership?.Guild);
                return;
            }

            if (recruiter.GuildInvite == client.ID)
            {
                recruiter.GuildInvite = 0;
                await JoinAsync(client, membership.Guild, recruiter);
                return;
            }
            client.GuildApply = recruiter.ID;
            await recruiter.SendAsync(new MsgRelation(client.Character, recruiter.ID));
            await recruiter.SendAsync(new MsgSyndicate(SyndicateAction.ApplyJoin, client.ID));
        }

        private static async Task InviteJoinAsync(Client client, uint targetID)
        {
            var membership = Of(client.ID);
            Kernel.Players.TryGet(targetID, out var target);
            string reason =
                membership == null || membership.Member.Rank < GuildRank.DeputyLeader ? "not_recruiter" :
                target?.Character == null || target == client ? "offline" :
                Of(target.ID) != null ? "in_guild" :
                CheckRequirements(membership.Guild, target);
            if (reason != null)
            {
                Reject(client, "invite", targetID.ToString(CultureInfo.InvariantCulture), reason);
                if (reason == "in_guild")
                    await Talk.NoticeAsync(client, $"{target.Character.Name} is already in a guild.");
                else if (reason is "level" or "rebirths" or "profession")
                    await Talk.NoticeAsync(client, $"{target.Character.Name} doesn't meet the guild's requirements.");
                return;
            }

            if (target.GuildApply == client.ID)
            {
                target.GuildApply = 0;
                await JoinAsync(target, membership.Guild, client);
                return;
            }
            client.GuildInvite = target.ID;
            await target.SendAsync(new MsgRelation(client.Character, target.ID));
            await target.SendAsync(new MsgSyndicate(SyndicateAction.InviteJoin, client.ID));
        }

        private static async Task JoinAsync(Client client, Guild guild, Client recruiter)
        {
            var character = client.Character;
            var member = new GuildMember(client.ID, character.Name, character.Level, GuildRank.Member, Guild.Today);
            await GuildsRepository.SaveMemberAsync(member.ToRow(guild.ID));
            guild.Members[client.ID] = member;
            ByMember[client.ID] = new Membership(guild, member);
            await RefreshAsync(client);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.GuildBattlePower,
                ArsenalConfig.SharedBattlePower(guild.Arsenal.TotalBattlePower, member.Rank)));
            await NotifyAsync(guild, $"{character.Name} has joined the guild.");
            LogEvents.For(typeof(Guilds), LogEvents.GuildJoined).Information(
                "{Name} joined guild {Guild} (guild {GuildId}, recruited by {Recruiter})",
                character.Name, guild.Name, guild.ID, recruiter.Character.Name);
        }

        private static async Task LeaveAsync(Client client)
        {
            var membership = Of(client.ID);
            if (membership == null || membership.Member.Rank == GuildRank.Leader)
            {
                Reject(client, "leave", membership?.Guild.Name, membership == null ? "no_guild" : "leader");
                if (membership != null)
                    await Talk.NoticeAsync(client, "The guild leader can't leave the guild. Pass on the leadership first.");
                return;
            }
            await RemoveAsync(membership.Guild, membership.Member, "left", null);
        }

        private static async Task RemoveAsync(Guild guild, GuildMember member, string reason, string by)
        {
            var removedItems = new List<(TotemPoleType Pole, uint ItemID)>();
            foreach (var pole in guild.Arsenal.Poles.Values)
            {
                foreach (var entry in pole.Items.Values.Where(e => e.CharacterID == member.CharacterID).ToList())
                {
                    pole.Items.Remove(entry.ItemID);
                    ByInscribedItem.TryRemove(entry.ItemID, out _);
                    removedItems.Add((pole.Type, entry.ItemID));
                }
            }
            if (removedItems.Count > 0)
                await GuildsRepository.RemoveMemberTotemsAsync(member.CharacterID);

            await GuildsRepository.RemoveMemberAsync(member.CharacterID);
            guild.Members.Remove(member.CharacterID);
            ByMember.TryRemove(member.CharacterID, out _);

            if (removedItems.Count > 0)
                await RecalculateArsenalAsync(guild);

            if (Online(member.CharacterID) is Client player)
            {
                foreach (var (poleType, itemID) in removedItems)
                    await player.SendAsync(new MsgTotemPole
                    {
                        Action = MsgTotemPole.TotemPoleAction.Remove,
                        TotemType = (uint)poleType,
                        ItemID = itemID,
                    });
                await player.SendAsync(new MsgUserAttrib(player.ID, MsgUserAttrib.AttributeType.GuildBattlePower, 0));
                await RefreshAsync(player);
                if (reason == "kicked")
                    await Talk.NoticeAsync(player, $"You have been expelled from the guild {guild.Name}.");
            }
            await NotifyAsync(guild, reason == "kicked"
                ? $"{member.Name} has been expelled from the guild by {by}."
                : $"{member.Name} has left the guild.");
            LogEvents.For(typeof(Guilds), LogEvents.GuildLeft).Information(
                "{Name} left guild {Guild} (guild {GuildId}, {Reason}, by {By})",
                member.Name, guild.Name, guild.ID, reason, by);
        }

        private static Task SendNameAsync(Client client, uint guildID)
        {
            if (!ById.TryGetValue(guildID, out var guild))
                return Task.CompletedTask;
            return client.SendAsync(new MsgName
            {
                Action = MsgName.NameAction.Guild,
                Data = guild.ID,
                Strings = new List<string> { guild.Summary },
            });
        }

        // Asking a guild whose leader asked first makes the alliance
        private static async Task AllyAsync(Client client, Guild target)
        {
            var guild = LedBy(client);
            Client targetLeader = target == null ? null : Online(target.LeaderID);
            string reason =
                guild == null ? "not_leader" :
                target == null || target == guild ? "no_guild" :
                guild.Allies.Contains(target.ID) ? "already_allied" :
                guild.Enemies.Contains(target.ID) ? "enemy" :
                guild.Allies.Count >= Guild.MaxAllies ? "allies_full" :
                target.Allies.Count >= Guild.MaxAllies ? "their_allies_full" :
                targetLeader == null ? "leader_offline" :
                null;
            if (reason != null)
            {
                Reject(client, "ally", target?.Name, reason);
                string notice = reason switch
                {
                    "enemy" => $"You must remove the guild {target.Name} from your enemies first.",
                    "allies_full" => "Your guild has too many allies.",
                    "their_allies_full" => $"The guild {target.Name} has too many allies.",
                    "leader_offline" => $"The leader of the guild {target.Name} is not online.",
                    _ => null,
                };
                if (notice != null)
                    await Talk.NoticeAsync(client, notice);
                return;
            }

            if (AllyRequests.TryGetValue(target.ID, out var asked) && asked == guild.ID)
            {
                AllyRequests.Remove(target.ID);
                guild.Allies.Add(target.ID);
                target.Allies.Add(guild.ID);
                await GuildsRepository.SetRelationAsync(guild.ID, target.ID, Guild.RelationAlly);
                await GuildsRepository.SetRelationAsync(target.ID, guild.ID, Guild.RelationAlly);
                await BroadcastAsync(guild, RelationEntry(MsgName.NameAction.SetAlly, target));
                await BroadcastAsync(target, RelationEntry(MsgName.NameAction.SetAlly, guild));
                await NotifyAsync(guild, $"The guild {target.Name} is now our ally.");
                await NotifyAsync(target, $"The guild {guild.Name} is now our ally.");
                LogRelation(client, guild, target, "ally");
                return;
            }
            AllyRequests[guild.ID] = target.ID;
            await targetLeader.SendAsync(new MsgSyndicate(SyndicateAction.AllyRequest, guild.ID, guild.Name));
            await Talk.NoticeAsync(client, $"You asked the guild {target.Name} for an alliance.");
        }

        private static async Task UnallyAsync(Client client, string name)
        {
            var guild = LedBy(client);
            var target = ById.Values.FirstOrDefault(g => Named(g, name));
            if (guild == null || target == null || !guild.Allies.Contains(target.ID))
            {
                Reject(client, "unally", name, guild == null ? "not_leader" : "not_allied");
                return;
            }
            guild.Allies.Remove(target.ID);
            target.Allies.Remove(guild.ID);
            await GuildsRepository.RemoveRelationAsync(guild.ID, target.ID);
            await GuildsRepository.RemoveRelationAsync(target.ID, guild.ID);
            await BroadcastAsync(guild, new MsgSyndicate(SyndicateAction.Unally, target.ID));
            await BroadcastAsync(target, new MsgSyndicate(SyndicateAction.Unally, guild.ID));
            await NotifyAsync(guild, $"The guild {target.Name} is no longer our ally.");
            await NotifyAsync(target, $"The guild {guild.Name} has ended our alliance.");
            LogRelation(client, guild, target, "unally");
        }

        private static async Task EnemyAsync(Client client, string name)
        {
            var guild = LedBy(client);
            var target = ById.Values.FirstOrDefault(g => Named(g, name));
            string reason =
                guild == null ? "not_leader" :
                target == null || target == guild ? "no_guild" :
                guild.Enemies.Contains(target.ID) ? "already_enemy" :
                guild.Allies.Contains(target.ID) ? "ally" :
                guild.Enemies.Count >= Guild.MaxEnemies ? "enemies_full" :
                null;
            if (reason != null)
            {
                Reject(client, "enemy", name, reason);
                if (reason == "ally")
                    await Talk.NoticeAsync(client, $"You must end the alliance with the guild {target.Name} first.");
                else if (reason == "no_guild")
                    await Talk.NoticeAsync(client, $"There is no guild named {name}.");
                return;
            }
            guild.Enemies.Add(target.ID);
            await GuildsRepository.SetRelationAsync(guild.ID, target.ID, Guild.RelationEnemy);
            await BroadcastAsync(guild, RelationEntry(MsgName.NameAction.SetEnemy, target));
            await NotifyAsync(guild, $"The guild {target.Name} is now our enemy.");
            await NotifyAsync(target, $"The guild {guild.Name} has made us their enemy.");
            LogRelation(client, guild, target, "enemy");
        }

        private static async Task UnenemyAsync(Client client, string name)
        {
            var guild = LedBy(client);
            var target = ById.Values.FirstOrDefault(g => Named(g, name));
            if (guild == null || target == null || !guild.Enemies.Remove(target.ID))
            {
                Reject(client, "unenemy", name, guild == null ? "not_leader" : "not_enemy");
                return;
            }
            await GuildsRepository.RemoveRelationAsync(guild.ID, target.ID);
            await BroadcastAsync(guild, new MsgSyndicate(SyndicateAction.Unenemy, target.ID));
            await NotifyAsync(guild, $"The guild {target.Name} is no longer our enemy.");
            await NotifyAsync(target, $"The guild {guild.Name} has removed us from their enemies.");
            LogRelation(client, guild, target, "unenemy");
        }

        private static async Task DonateAsync(Client client, uint amount, bool conquerPoints)
        {
            var membership = Of(client.ID);
            string kind = conquerPoints ? "donate_cps" : "donate_silver";
            if (membership == null || amount == 0)
            {
                Reject(client, kind, membership?.Guild.Name, membership == null ? "no_guild" : "amount");
                return;
            }
            bool paid = conquerPoints
                ? await Progress.ChangeConquerPointsAsync(client, -amount, "guild_donation")
                : await Progress.ChangeSilverAsync(client, -amount, "guild_donation");
            if (!paid)
            {
                Reject(client, kind, membership.Guild.Name, conquerPoints ? "cps" : "silver");
                await Talk.NoticeAsync(client, conquerPoints ? "You don't have enough CPs." : "You don't have enough silver.");
                return;
            }

            var guild = membership.Guild;
            var member = membership.Member;
            if (conquerPoints)
            {
                guild.ConquerPointsFund = Add(guild.ConquerPointsFund, amount);
                member.ConquerPointsDonation = Add(member.ConquerPointsDonation, amount);
            }
            else
            {
                guild.Fund += amount;
                member.SilverDonation = Add(member.SilverDonation, amount);
            }
            await GuildsRepository.SaveAsync(guild.ToRow());
            await GuildsRepository.SaveMemberAsync(member.ToRow(guild.ID));
            await client.SendAsync(new MsgSyndicateAttributeInfo(guild, member));
            await client.SendAsync(new MsgSynpOffer(member));
            LogEvents.For(typeof(Guilds), LogEvents.GuildDonated).Information(
                "{Name} donated {Amount} {Currency} to guild {Guild} (guild {GuildId}, fund {Fund}, CP fund {ConquerPointsFund})",
                client.Character.Name, amount, conquerPoints ? "cps" : "silver", guild.Name, guild.ID,
                guild.Fund, guild.ConquerPointsFund);
        }

        private static async Task SetRequirementsAsync(Client client, MsgSyndicate msg)
        {
            var guild = LedBy(client);
            if (guild == null)
            {
                Reject(client, "requirements", null, "not_leader");
                return;
            }
            guild.RequiredLevel = Math.Clamp(msg.RequiredLevel, 1, 140);
            guild.RequiredRebirths = Math.Min(msg.RequiredRebirths, 2);
            guild.RequiredProfessions = msg.RequiredProfessions & 0x7F;
            await GuildsRepository.SaveAsync(guild.ToRow());
            await client.SendAsync(new MsgSyndicateAttributeInfo(guild, Of(client.ID)?.Member));
        }

        private static async Task AnnounceAsync(Client client, string text)
        {
            var guild = LedBy(client);
            if (guild == null)
            {
                Reject(client, "announce", null, "not_leader");
                await Talk.NoticeAsync(client, "Only the guild leader can change the announcement.");
                return;
            }
            guild.Announcement = text.Length > MaxAnnouncement ? text[..MaxAnnouncement] : text;
            guild.AnnouncementDate = Guild.Today;
            await GuildsRepository.SaveAsync(guild.ToRow());
            await BroadcastAsync(guild, new MsgSyndicate(SyndicateAction.Announce, guild.AnnouncementDate, guild.Announcement));
            LogEvents.For(typeof(Guilds), LogEvents.GuildAnnounced).Information(
                "{Name} set the announcement of guild {Guild} (guild {GuildId}): {Text}",
                client.Character.Name, guild.Name, guild.ID, guild.Announcement);
        }

        private static async Task SetRankAsync(Client client, string name, GuildRank rank)
        {
            var guild = LedBy(client);
            var target = guild?.Find(name);
            string reason =
                guild == null ? "not_leader" :
                target == null ? "not_member" :
                target.CharacterID == client.ID ? "self" :
                target.Rank == rank ? "same_rank" :
                rank == GuildRank.DeputyLeader && guild.Deputies >= Guild.MaxDeputies ? "deputies_full" :
                null;
            if (reason != null)
            {
                Reject(client, rank == GuildRank.Member ? "discharge" : "promote", name, reason);
                if (reason == "deputies_full")
                    await Talk.NoticeAsync(client, "All the deputy leader posts are taken.");
                return;
            }
            target.Rank = rank;
            await GuildsRepository.SaveMemberAsync(target.ToRow(guild.ID));
            if (Online(target.CharacterID) is Client player)
            {
                await RefreshAsync(player);
                await player.SendAsync(new MsgUserAttrib(player.ID, MsgUserAttrib.AttributeType.GuildBattlePower,
                    ArsenalConfig.SharedBattlePower(guild.Arsenal.TotalBattlePower, target.Rank)));
            }
            await NotifyAsync(guild, rank == GuildRank.DeputyLeader
                ? $"{client.Character.Name} has appointed {target.Name} as deputy leader."
                : $"{target.Name} is no longer a deputy leader.");
            LogRank(client, guild, target, rank == GuildRank.Member ? "discharge" : "promote");
        }

        // Each position: rank, taken, posts, then two zeros
        private static Task SendPromotionListAsync(Client client)
        {
            var guild = Of(client.ID)?.Guild;
            if (guild == null)
                return Task.CompletedTask;
            return client.SendAsync(new MsgSyndicate(SyndicateAction.PromotionList, 0,
                $"{(int)GuildRank.Leader} 1 1 0 0",
                $"{(int)GuildRank.DeputyLeader} {guild.Deputies} {Guild.MaxDeputies} 0 0"));
        }

        // The guild window for the player: which guild, the attributes, the announcement,
        // allies and enemies; just the empty attributes without a guild
        private static async Task SendInfoAsync(Client client)
        {
            var membership = Of(client.ID);
            if (membership == null)
            {
                await client.SendAsync(new MsgSyndicateAttributeInfo(null, null));
                return;
            }
            var guild = membership.Guild;
            await client.SendAsync(new MsgSyndicate(SyndicateAction.SetSyndicate, guild.ID));
            await client.SendAsync(new MsgSyndicateAttributeInfo(guild, membership.Member));
            await client.SendAsync(new MsgSyndicate(SyndicateAction.Announce, guild.AnnouncementDate, guild.Announcement));
            foreach (var id in guild.Allies)
                if (ById.TryGetValue(id, out var ally))
                    await client.SendAsync(RelationEntry(MsgName.NameAction.SetAlly, ally));
            foreach (var id in guild.Enemies)
                if (ById.TryGetValue(id, out var enemy))
                    await client.SendAsync(RelationEntry(MsgName.NameAction.SetEnemy, enemy));
        }

        private static async Task RefreshAsync(Client client)
        {
            await SendInfoAsync(client);
            await Movement.SendToViewAsync(client, new MsgPlayer(client));
        }

        private static MsgName RelationEntry(MsgName.NameAction action, Guild guild) => new MsgName
        {
            Action = action,
            Data = guild.ID,
            Strings = new List<string> { guild.Summary },
        };

        private static string CheckRequirements(Guild guild, Client client)
        {
            var character = client.Character;
            return
                character.Level < guild.RequiredLevel ? "level" :
                character.Rebirths < guild.RequiredRebirths ? "rebirths" :
                (guild.RequiredProfessions & Guild.ProfessionBit(character.CurrentClass)) != 0 ? "profession" :
                null;
        }

        private static Task NoticeRejectAsync(Client client, string reason, Guild guild) => reason switch
        {
            "in_guild" => Talk.NoticeAsync(client, "You are already in a guild."),
            "not_recruiter" => Talk.NoticeAsync(client, "Ask a guild leader or deputy leader to join a guild."),
            "level" => Talk.NoticeAsync(client, $"You must be level {guild.RequiredLevel} to join the guild {guild.Name}."),
            "rebirths" => Talk.NoticeAsync(client, $"You must be reborn {guild.RequiredRebirths} times to join the guild {guild.Name}."),
            "profession" => Talk.NoticeAsync(client, $"The guild {guild.Name} doesn't accept your class."),
            _ => Task.CompletedTask,
        };

        // Guild names: like character names but shorter allowed, and no spaces, which would
        // split the name where the client reads it
        public static string CheckName(string name) =>
            name.Length < 2 || name.Length > MaxNameLength || !name.All(c => c > ' ' && c < 0x7F) ||
            name.Contains("[GM]", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("[PM]", StringComparison.OrdinalIgnoreCase)
                ? "invalid_name" : null;

        private static Guild LedBy(Client client) =>
            Of(client.ID) is Membership m && m.Member.Rank == GuildRank.Leader ? m.Guild : null;

        private static bool Named(Guild guild, string name) =>
            string.Equals(guild.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static Client Online(uint characterID) =>
            Kernel.Players.TryGet(characterID, out var player) && player.Character != null ? player : null;

        private static uint Add(uint value, uint amount) => (uint)Math.Min((ulong)value + amount, uint.MaxValue);

        private static async Task BroadcastAsync(Guild guild, IPacket packet)
        {
            foreach (var id in guild.Members.Keys)
                if (Online(id) is Client player)
                    await player.SendAsync(packet);
        }

        private static async Task NotifyAsync(Guild guild, string text)
        {
            foreach (var id in guild.Members.Keys)
                if (Online(id) is Client player)
                    await player.SendAsync(new MsgTalk(player.ID, MsgTalk.TalkChannel.Guild, text));
        }

        private static void LogRelation(Client client, Guild guild, Guild target, string kind) =>
            LogEvents.For(typeof(Guilds), LogEvents.GuildRelation).Information(
                "{Name} set {Kind} between guild {Guild} and {Target} (guilds {GuildId} and {TargetId})",
                client.Character.Name, kind, guild.Name, target.Name, guild.ID, target.ID);

        private static void LogRank(Client client, Guild guild, GuildMember target, string kind) =>
            LogEvents.For(typeof(Guilds), LogEvents.GuildRank).Information(
                "{Name} made {Member} {Rank} in guild {Guild} (guild {GuildId}, {Kind})",
                client.Character.Name, target.Name, target.Rank.ToString(), guild.Name, guild.ID, kind);

        private static void Reject(Client client, string kind, string target, string reason) =>
            LogEvents.For(typeof(Guilds), LogEvents.GuildRejected).Warning(
                "{Name} can't {Kind} {Target}: {Reason}", client.Character?.Name, kind, target, reason);

        public static async Task HandleTotemPoleAsync(Client client, MsgTotemPole msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case MsgTotemPole.TotemPoleAction.Unlock:
                        await UnlockArsenalAsync(client, msg.TotemType);
                        break;
                    case MsgTotemPole.TotemPoleAction.Inscribe:
                        await InscribeItemAsync(client, msg.TotemType, msg.ItemID);
                        break;
                    case MsgTotemPole.TotemPoleAction.Remove:
                        await RemoveInscribedItemAsync(client, msg.TotemType, msg.ItemID);
                        break;
                    case MsgTotemPole.TotemPoleAction.Enhance:
                        await EnhanceArsenalAsync(client, msg.TotemType, msg.Enhancement);
                        break;
                    case MsgTotemPole.TotemPoleAction.Refresh:
                        await SendArsenalInfoAsync(client);
                        break;
                    default:
                        LogEvents.For(typeof(Guilds), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                            msg.Type, msg.Action, msg.Length, Convert.ToHexString(msg.Encode()));
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task SendWeaponsInfoAsync(Client client, MsgWeaponsInfo msg)
        {
            if (client.Character == null || msg.TotemType >= ArsenalConfig.PoleCount)
                return;
            await Gate.WaitAsync();
            try
            {
                var guild = Of(client.ID)?.Guild;
                if (guild == null)
                    return;
                var poleType = (TotemPoleType)msg.TotemType;
                var pole = guild.Arsenal.Get(poleType);
                if (pole == null)
                    return;

                uint today = Guild.Today;
                var config = Kernel.ArsenalConfig;
                var ordered = pole.Items.Values
                    .OrderByDescending(e => e.Donation(config))
                    .ThenBy(e => e.ItemID)
                    .ToList();

                uint beginAt = Math.Max(1u, msg.BeginAt);
                uint endAt = Math.Max(beginAt, msg.EndAt);
                var reply = new MsgWeaponsInfo
                {
                    Action = 0,
                    BeginAt = beginAt,
                    EndAt = endAt,
                    TotemType = msg.TotemType,
                    TotalCount = (uint)ordered.Count,
                    BattlePower = pole.DonationBattlePower,
                    Enhancement = pole.ActiveEnhancement(today),
                    EnhancementExpiration = pole.ActiveExpiration(today),
                    Donation = (uint)Math.Min(pole.Donation, uint.MaxValue),
                };

                for (uint pos = beginAt; pos <= endAt && pos <= (uint)ordered.Count; pos++)
                {
                    var entry = ordered[(int)(pos - 1)];
                    reply.Items.Add(new MsgWeaponsInfo.WeaponEntry(
                        ItemID: entry.ItemID,
                        Position: pos,
                        OwnerName: entry.OwnerName,
                        TypeID: entry.TypeID,
                        Quality: (byte)ItemSlots.Quality(entry.TypeID),
                        Plus: entry.Plus,
                        Gem1: entry.Gem1,
                        Gem2: entry.Gem2,
                        BattlePower: entry.BattlePower,
                        Donation: entry.Donation(config)));
                }
                await client.SendAsync(reply);
            }
            finally { Gate.Release(); }
        }

        public static async Task RefreshInscribedItemAsync(DbItem item)
        {
            if (item == null || !ByInscribedItem.TryGetValue(item.ItemID, out var guildID))
                return;
            await Gate.WaitAsync();
            try
            {
                if (!ById.TryGetValue(guildID, out var guild))
                    return;
                foreach (var pole in guild.Arsenal.Poles.Values)
                {
                    if (!pole.Items.TryGetValue(item.ItemID, out var entry))
                        continue;
                    entry.TypeID = item.TypeID;
                    entry.Plus = item.Plus;
                    entry.Gem1 = item.Gem1;
                    entry.Gem2 = item.Gem2;
                    await RecalculateArsenalAsync(guild);
                    return;
                }
            }
            finally { Gate.Release(); }
        }

        private static async Task UnlockArsenalAsync(Client client, uint rawType)
        {
            var membership = Of(client.ID);
            if (membership == null || membership.Member.Rank < GuildRank.DeputyLeader)
            {
                Reject(client, "unlock_arsenal", rawType.ToString(CultureInfo.InvariantCulture), "not_authorized");
                await Talk.NoticeAsync(client, "Only the guild leader or deputy leader can unlock an Arsenal.");
                return;
            }
            if (rawType >= ArsenalConfig.PoleCount)
            {
                Reject(client, "unlock_arsenal", rawType.ToString(CultureInfo.InvariantCulture), "invalid_type");
                return;
            }
            var guild = membership.Guild;
            var poleType = (TotemPoleType)rawType;
            if (guild.Arsenal.Get(poleType) != null)
            {
                Reject(client, "unlock_arsenal", poleType.ToString(), "already_open");
                return;
            }
            uint today = Guild.Today;
            if (guild.LastOpenTotem == today)
            {
                Reject(client, "unlock_arsenal", poleType.ToString(), "daily_limit");
                await Talk.NoticeAsync(client, "Each guild can only unlock one Arsenal per day.");
                return;
            }
            ulong cost = ArsenalConfig.UnlockCost(guild.Arsenal.UnlockedCount + 1);
            if (guild.Fund < cost)
            {
                Reject(client, "unlock_arsenal", poleType.ToString(), "fund");
                await Talk.NoticeAsync(client, "You don't have enough Guild Fund.");
                return;
            }

            guild.Fund -= cost;
            guild.LastOpenTotem = today;
            var pole = guild.Arsenal.Unlock(poleType);
            await GuildsRepository.SaveTotemPoleAsync(pole.ToRow(guild.ID));
            await GuildsRepository.SaveAsync(guild.ToRow());
            await SendArsenalInfoAsync(client);
            await client.SendAsync(new MsgSyndicateAttributeInfo(guild, membership.Member));
            await NotifyAsync(guild, $"{client.Character.Name} has unlocked the {poleType} Arsenal.");
            LogEvents.For(typeof(Guilds), LogEvents.GuildArsenalUnlocked).Information(
                "{Name} unlocked {Pole} arsenal for guild {Guild} (guild {GuildId}, cost {Cost}, fund {Fund})",
                client.Character.Name, poleType, guild.Name, guild.ID, cost, guild.Fund);
        }

        private static async Task InscribeItemAsync(Client client, uint rawType, uint itemID)
        {
            var membership = Of(client.ID);
            if (membership == null)
            {
                Reject(client, "inscribe", itemID.ToString(CultureInfo.InvariantCulture), "no_guild");
                return;
            }
            var item = FindCarriedItem(client, itemID);
            if (item == null)
            {
                Reject(client, "inscribe", itemID.ToString(CultureInfo.InvariantCulture), "not_found");
                return;
            }
            var poleType = ArsenalConfig.PoleTypeOf(item.TypeID);
            if (poleType == null || (rawType < ArsenalConfig.PoleCount && (TotemPoleType)rawType != poleType.Value))
            {
                Reject(client, "inscribe", itemID.ToString(CultureInfo.InvariantCulture), "wrong_type");
                return;
            }
            if (ItemSlots.Quality(item.TypeID) is not (8 or 9))
            {
                Reject(client, "inscribe", itemID.ToString(CultureInfo.InvariantCulture), "quality");
                await Talk.NoticeAsync(client, "Only Elite or above equipment can be inscribed in the Martial Arsenal.");
                return;
            }
            if (ByInscribedItem.ContainsKey(item.ItemID))
            {
                Reject(client, "inscribe", itemID.ToString(CultureInfo.InvariantCulture), "already_inscribed");
                return;
            }
            var guild = membership.Guild;
            var pole = guild.Arsenal.Get(poleType.Value);
            if (pole == null)
            {
                Reject(client, "inscribe", poleType.Value.ToString(), "locked");
                await Talk.NoticeAsync(client, $"The {poleType.Value} Arsenal is locked.");
                return;
            }

            var entry = new TotemEntry(
                item.ItemID, client.ID, client.Character.Name, poleType.Value,
                item.TypeID, item.Plus, item.Gem1, item.Gem2);
            await GuildsRepository.AddTotemAsync(new DbSyndicateTotem
            {
                ItemID = item.ItemID,
                SyndicateID = guild.ID,
                CharacterID = client.ID,
                Type = (byte)poleType.Value,
            });
            pole.Items[item.ItemID] = entry;
            ByInscribedItem[item.ItemID] = guild.ID;

            await RecalculateArsenalAsync(guild);
            await client.SendAsync(new MsgTotemPole
            {
                Action = MsgTotemPole.TotemPoleAction.Inscribe,
                TotemType = (uint)poleType.Value,
                ItemID = item.ItemID,
            });
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Update));
            await SendArsenalInfoAsync(client);
            await client.SendAsync(new MsgSynpOffer(membership.Member));
            LogEvents.For(typeof(Guilds), LogEvents.GuildArsenalInscribed).Information(
                "{Name} inscribed item {ItemId} (type {TypeId}, donation {Donation}) into {Pole} arsenal of guild {Guild} (guild {GuildId})",
                client.Character.Name, item.ItemID, item.TypeID, entry.Donation(Kernel.ArsenalConfig),
                poleType.Value, guild.Name, guild.ID);
        }

        private static async Task RemoveInscribedItemAsync(Client client, uint rawType, uint itemID)
        {
            var membership = Of(client.ID);
            if (membership == null)
            {
                Reject(client, "remove_inscribed", itemID.ToString(CultureInfo.InvariantCulture), "no_guild");
                return;
            }
            var guild = membership.Guild;
            TotemPole pole = null;
            TotemEntry entry = null;
            if (rawType < ArsenalConfig.PoleCount &&
                guild.Arsenal.Get((TotemPoleType)rawType) is TotemPole candidate &&
                candidate.Items.TryGetValue(itemID, out entry))
            {
                pole = candidate;
            }
            else
            {
                foreach (var p in guild.Arsenal.Poles.Values)
                {
                    if (p.Items.TryGetValue(itemID, out entry))
                    {
                        pole = p;
                        break;
                    }
                }
            }
            if (pole == null || entry == null)
            {
                Reject(client, "remove_inscribed", itemID.ToString(CultureInfo.InvariantCulture), "not_inscribed");
                return;
            }
            if (entry.CharacterID != client.ID)
            {
                Reject(client, "remove_inscribed", itemID.ToString(CultureInfo.InvariantCulture), "not_owner");
                return;
            }

            await GuildsRepository.RemoveTotemAsync(itemID);
            pole.Items.Remove(itemID);
            ByInscribedItem.TryRemove(itemID, out _);

            await RecalculateArsenalAsync(guild);
            await client.SendAsync(new MsgTotemPole
            {
                Action = MsgTotemPole.TotemPoleAction.Remove,
                TotemType = (uint)pole.Type,
                ItemID = itemID,
            });
            if (FindCarriedItem(client, itemID) is DbItem carried)
                await client.SendAsync(new MsgItemInfo(carried, MsgItemInfo.ItemAction.Update));
            await SendArsenalInfoAsync(client);
            await client.SendAsync(new MsgSynpOffer(membership.Member));
            LogEvents.For(typeof(Guilds), LogEvents.GuildArsenalRemoved).Information(
                "{Name} removed item {ItemId} from {Pole} arsenal of guild {Guild} (guild {GuildId})",
                client.Character.Name, itemID, pole.Type, guild.Name, guild.ID);
        }

        private static async Task EnhanceArsenalAsync(Client client, uint rawType, uint addBp)
        {
            var membership = Of(client.ID);
            if (membership == null || membership.Member.Rank < GuildRank.DeputyLeader)
            {
                Reject(client, "enhance_arsenal", rawType.ToString(CultureInfo.InvariantCulture), "not_authorized");
                return;
            }
            if (rawType >= ArsenalConfig.PoleCount || addBp is not (1 or 2))
            {
                Reject(client, "enhance_arsenal", rawType.ToString(CultureInfo.InvariantCulture), "invalid_args");
                return;
            }
            var guild = membership.Guild;
            var pole = guild.Arsenal.Get((TotemPoleType)rawType);
            if (pole == null)
            {
                Reject(client, "enhance_arsenal", ((TotemPoleType)rawType).ToString(), "locked");
                return;
            }
            if (pole.DonationBattlePower >= ArsenalConfig.MaxPoleBattlePower)
            {
                Reject(client, "enhance_arsenal", pole.Type.ToString(), "already_max");
                return;
            }
            ulong cost = ArsenalConfig.EnhanceCost((int)addBp);
            if (guild.Fund < cost)
            {
                Reject(client, "enhance_arsenal", pole.Type.ToString(), "fund");
                await Talk.NoticeAsync(client, "You don't have enough Guild Fund.");
                return;
            }

            uint expiry = uint.Parse(Clock.Local(DateTime.UtcNow).AddDays(ArsenalConfig.EnhancementDays).ToString("yyyyMMdd"));
            guild.Fund -= cost;
            pole.Enhancement = (byte)addBp;
            pole.EnhancementExpiration = expiry;
            await GuildsRepository.SaveTotemPoleAsync(pole.ToRow(guild.ID));
            await GuildsRepository.SaveAsync(guild.ToRow());
            await RecalculateArsenalAsync(guild);

            await client.SendAsync(new MsgTotemPole
            {
                Action = MsgTotemPole.TotemPoleAction.Enhance,
                TotemType = (uint)pole.Type,
                Enhancement = addBp,
                EnhancementExpiration = expiry,
            });
            await SendArsenalInfoAsync(client);
            await client.SendAsync(new MsgSyndicateAttributeInfo(guild, membership.Member));
            LogEvents.For(typeof(Guilds), LogEvents.GuildArsenalEnhanced).Information(
                "{Name} enhanced {Pole} arsenal of guild {Guild} (guild {GuildId}) by +{Bp} until {Expiry} for {Cost}",
                client.Character.Name, pole.Type, guild.Name, guild.ID, addBp, expiry, cost);
        }

        private static async Task SendArsenalInfoAsync(Client client)
        {
            var membership = Of(client.ID);
            if (membership == null)
                return;
            var guild = membership.Guild;
            uint today = Guild.Today;
            guild.Arsenal.Recalculate(Kernel.ArsenalConfig, today, guild.Members);
            uint sharedBp = ArsenalConfig.SharedBattlePower(guild.Arsenal.TotalBattlePower, membership.Member.Rank);

            var info = new MsgTotemPoleInfo
            {
                TotalBattlePower = guild.Arsenal.TotalBattlePower,
                PlayerDonation = membership.Member.ArsenalDonation,
                SharedBattlePower = sharedBp,
            };
            for (int i = 0; i < ArsenalConfig.PoleCount; i++)
            {
                var type = (TotemPoleType)i;
                var pole = guild.Arsenal.Get(type);
                if (pole == null)
                {
                    info.Poles.Add(new MsgTotemPoleInfo.PoleEntry(type, 0, 0, 0, 0, false));
                    continue;
                }
                info.Poles.Add(new MsgTotemPoleInfo.PoleEntry(
                    type,
                    pole.DonationBattlePower,
                    pole.ActiveEnhancement(today),
                    (uint)Math.Min(pole.Donation, uint.MaxValue),
                    pole.ActiveExpiration(today),
                    true));
            }
            await client.SendAsync(info);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.GuildBattlePower, sharedBp));
        }

        private static async Task RecalculateArsenalAsync(Guild guild)
        {
            uint oldBp = guild.Arsenal.TotalBattlePower;
            byte oldLevel = guild.Level;
            guild.Arsenal.Recalculate(Kernel.ArsenalConfig, Guild.Today, guild.Members);
            guild.Level = guild.Arsenal.GuildLevel;
            if (guild.Level != oldLevel)
                await GuildsRepository.SaveAsync(guild.ToRow());

            if (guild.Arsenal.TotalBattlePower != oldBp || guild.Level != oldLevel)
            {
                foreach (var member in guild.Members.Values)
                {
                    if (Online(member.CharacterID) is not Client player)
                        continue;
                    if (guild.Arsenal.TotalBattlePower != oldBp)
                    {
                        uint sharedBp = ArsenalConfig.SharedBattlePower(guild.Arsenal.TotalBattlePower, member.Rank);
                        await player.SendAsync(new MsgUserAttrib(
                            player.ID, MsgUserAttrib.AttributeType.GuildBattlePower, sharedBp));
                    }
                    if (guild.Level != oldLevel)
                        await player.SendAsync(new MsgSyndicateAttributeInfo(guild, member));
                }
            }
        }

        private static DbItem FindCarriedItem(Client client, uint itemID) =>
            client.Inventory?.Find(itemID) ??
            client.Equipment?.Items.FirstOrDefault(i => i.ItemID == itemID);
    }
}

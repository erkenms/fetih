namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Admin;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgSelfSynMemAwardRank;

    // A guild's reward pool (help "Guild Reward" and "Capture Flag"): before Capture the Flag, or
    // the next Guild Contest or Guild PK Tournament match, the leader moves CPs and silver from
    // the guild's funds into a pool, at least 100 of a currency once it's in, and can only add.
    // When that event ends the pool is split among the guild's members by the exploits each
    // earned in it; members see the split and each share in the guild's reward window. A pool
    // waits for the next event if nobody of the guild earned exploits.
    public static class GuildRewards
    {
        public const uint MinimumReward = 100;
        public const int PageSize = 8;
        // The Capture the Flag shares window pages by 4
        public const int CaptureTheFlagSharesPage = 4;

        private sealed class Tally
        {
            public uint GuildID;
            public string Name;
            public uint Exploits;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<(uint GuildID, AwardEvent Event), (uint ConquerPoints, ulong Silver)> Pools =
            new Dictionary<(uint, AwardEvent), (uint, ulong)>();
        private static readonly Dictionary<(uint GuildID, AwardEvent Event), List<DbGuildRewardShare>> Splits =
            new Dictionary<(uint, AwardEvent), List<DbGuildRewardShare>>();
        private static readonly Dictionary<AwardEvent, Dictionary<uint, Tally>> Tallies =
            new Dictionary<AwardEvent, Dictionary<uint, Tally>>();

        public static async Task LoadAsync()
        {
            var (pools, shares) = await GuildRewardsRepository.LoadAsync();
            lock (Sync)
            {
                Pools.Clear();
                Splits.Clear();
                foreach (var row in pools)
                    Pools[(row.SyndicateID, (AwardEvent)row.Event)] = (row.ConquerPoints, row.Silver);
                foreach (var group in shares.GroupBy(s => (s.SyndicateID, (AwardEvent)s.Event)))
                    Splits[group.Key] = group.OrderByDescending(s => s.Exploits).ToList();
            }
            LogEvents.For(typeof(GuildRewards), LogEvents.GuildRewardLoaded).Information(
                "Loaded {PoolCount} guild reward pools", pools.Count);
        }

        public static (uint ConquerPoints, ulong Silver) PoolOf(uint guildID, AwardEvent ev)
        {
            lock (Sync)
                return Pools.GetValueOrDefault((guildID, ev));
        }

        public static void Forget(uint guildID)
        {
            lock (Sync)
            {
                foreach (var key in Pools.Keys.Where(k => k.GuildID == guildID).ToList())
                    Pools.Remove(key);
                foreach (var key in Splits.Keys.Where(k => k.GuildID == guildID).ToList())
                    Splits.Remove(key);
            }
        }

        // ---- Exploits earned in the running event ----

        public static void Begin(AwardEvent ev)
        {
            lock (Sync)
                Tallies[ev] = new Dictionary<uint, Tally>();
        }

        public static uint AddExploits(AwardEvent ev, uint guildID, uint characterID, string name, int exploits)
        {
            lock (Sync)
            {
                if (!Tallies.TryGetValue(ev, out var tally))
                    Tallies[ev] = tally = new Dictionary<uint, Tally>();
                if (!tally.TryGetValue(characterID, out var t))
                    tally[characterID] = t = new Tally { GuildID = guildID, Name = name };
                t.Exploits += (uint)Math.Max(0, exploits);
                return t.Exploits;
            }
        }

        public static uint ExploitsOf(AwardEvent ev, uint characterID)
        {
            lock (Sync)
                return Tallies.GetValueOrDefault(ev)?.GetValueOrDefault(characterID)?.Exploits ?? 0;
        }

        // ---- Rules ----

        // Null when the guild may add these amounts to its pool, else why not
        public static string CheckAdd((uint ConquerPoints, ulong Silver) pool, (uint ConquerPoints, ulong Silver) add,
            uint fundConquerPoints, ulong fundSilver)
        {
            if (add.ConquerPoints == 0 && add.Silver == 0)
                return "amount";
            ulong cps = (ulong)pool.ConquerPoints + add.ConquerPoints;
            if (cps > uint.MaxValue || pool.Silver + add.Silver < pool.Silver)
                return "amount";
            if ((cps != 0 && cps < MinimumReward) || (pool.Silver + add.Silver != 0 && pool.Silver + add.Silver < MinimumReward))
                return "minimum";
            if (add.ConquerPoints > fundConquerPoints || add.Silver > fundSilver)
                return "funds";
            return null;
        }

        // Each member's share of the pool by exploits, rounded down; what doesn't divide stays out
        public static List<(uint CharacterID, uint ConquerPoints, ulong Silver)> Split(uint conquerPoints, ulong silver,
            IReadOnlyList<(uint CharacterID, uint Exploits)> exploits)
        {
            ulong total = 0;
            foreach (var (_, e) in exploits)
                total += e;
            var shares = new List<(uint, uint, ulong)>();
            if (total == 0)
                return shares;
            foreach (var (id, e) in exploits)
                shares.Add((id, (uint)((UInt128)conquerPoints * e / total), (ulong)((UInt128)silver * e / total)));
            return shares;
        }

        // ---- The client ----

        public static async Task HandleAsync(Client client, MsgSelfSynMemAwardRank msg)
        {
            if (client.Character == null)
                return;
            if (msg.Event != AwardEvent.CaptureTheFlag && msg.Event != AwardEvent.GuildPk)
            {
                Unhandled(msg);
                return;
            }
            switch (msg.Action)
            {
                case AwardAction.GuildPools: await client.SendAsync(PoolsPacket(client.ID, msg.Event, msg.Page)); break;
                case AwardAction.MemberShares: await client.SendAsync(SharesPacket(client.ID, msg.Event, msg.Page)); break;
                case AwardAction.Exploits when msg.Event == AwardEvent.CaptureTheFlag:
                    await client.SendAsync(ExploitsPacket(client.ID, msg.Event, msg.Page));
                    break;
                case var _ when msg.IsAdd: await AddAsync(client, msg); break;
                default: Unhandled(msg); break;
            }
        }

        private static async Task AddAsync(Client client, MsgSelfSynMemAwardRank msg)
        {
            var add = msg.Added;
            var (reason, guild, pool) = await Guilds.ChangeAsync(async () =>
            {
                var membership = Guilds.Of(client.ID);
                if (membership == null)
                    return ("no_guild", (Guild)null, default((uint, ulong)));
                var g = membership.Guild;
                if (membership.Member.Rank != GuildRank.Leader)
                    return ("not_leader", g, default);
                var before = PoolOf(g.ID, msg.Event);
                var problem = CheckAdd(before, add, g.ConquerPointsFund, g.Fund);
                if (problem != null)
                    return (problem, g, before);
                var after = (before.ConquerPoints + add.ConquerPoints, before.Silver + add.Silver);
                g.ConquerPointsFund -= add.ConquerPoints;
                g.Fund -= add.Silver;
                try
                {
                    await GuildRewardsRepository.SaveAsync(g.ToRow(), new DbGuildRewardPool
                    {
                        SyndicateID = g.ID, Event = (byte)msg.Event, ConquerPoints = after.Item1, Silver = after.Item2,
                    });
                }
                catch
                {
                    g.ConquerPointsFund += add.ConquerPoints;
                    g.Fund += add.Silver;
                    throw;
                }
                lock (Sync)
                    Pools[(g.ID, msg.Event)] = after;
                return (null, g, after);
            });

            if (reason != null)
            {
                LogEvents.For(typeof(GuildRewards), LogEvents.GuildRewardRejected).Information(
                    "{Name} can't add {ConquerPoints} CPs and {Silver} silver to guild {Guild}'s {RewardEvent} reward: {Reason}",
                    client.Character.Name, add.ConquerPoints, add.Silver, guild?.Name, msg.Event, reason);
                string text = reason switch
                {
                    "minimum" => "Rewards should more than 100",
                    "funds" => "Insufficient Guild Funds",
                    "not_leader" => "Only the guild leader can set the reward.",
                    "no_guild" => "You are not in a guild.",
                    _ => null,
                };
                if (text != null)
                    await Talk.NoticeAsync(client, text);
                return;
            }

            await client.SendAsync(new MsgSelfSynMemAwardRank(msg.Action, msg.Event)
            {
                ConquerPoints = pool.Item1, Silver = pool.Item2,
            });
            await client.SendAsync(new MsgSyndicateAttributeInfo(guild, Guilds.Of(client.ID)?.Member));
            LogEvents.For(typeof(GuildRewards), LogEvents.GuildRewardAdded).Information(
                "{Name} added {ConquerPoints} CPs and {Silver} silver to guild {Guild} ({GuildId})'s {RewardEvent} reward, now {PoolConquerPoints} CPs and {PoolSilver} silver",
                client.Character.Name, add.ConquerPoints, add.Silver, guild.Name, guild.ID, msg.Event, pool.Item1, pool.Item2);
        }

        // Every guild's pool, most CPs first, then most silver
        public static MsgSelfSynMemAwardRank PoolsPacket(uint characterID, AwardEvent ev, uint page)
        {
            var guildID = Guilds.Of(characterID)?.Guild.ID ?? 0;
            List<(uint, ulong, string)> pools;
            (uint ConquerPoints, ulong Silver) own;
            lock (Sync)
            {
                own = Pools.GetValueOrDefault((guildID, ev));
                pools = Pools.Where(p => p.Key.Event == ev && (p.Value.ConquerPoints > 0 || p.Value.Silver > 0))
                    .Select(p => (p.Value.ConquerPoints, p.Value.Silver, Guilds.Find(p.Key.GuildID)?.Name))
                    .Where(p => p.Item3 != null)
                    .OrderByDescending(p => p.Item1).ThenByDescending(p => p.Item2).ThenBy(p => p.Item3, StringComparer.Ordinal)
                    .ToList();
            }
            var msg = new MsgSelfSynMemAwardRank(AwardAction.GuildPools, ev)
            {
                Total = (uint)pools.Count, ConquerPoints = own.ConquerPoints, Silver = own.Silver,
            };
            msg.Pools.AddRange(OnPage(pools, page, PageSize));
            return msg;
        }

        // The guild's last split, most exploits first
        public static MsgSelfSynMemAwardRank SharesPacket(uint characterID, AwardEvent ev, uint page)
        {
            var guildID = Guilds.Of(characterID)?.Guild.ID ?? 0;
            List<DbGuildRewardShare> shares;
            (uint ConquerPoints, ulong Silver) own;
            lock (Sync)
            {
                own = Pools.GetValueOrDefault((guildID, ev));
                shares = Splits.GetValueOrDefault((guildID, ev))?.ToList() ?? new List<DbGuildRewardShare>();
            }
            var msg = new MsgSelfSynMemAwardRank(AwardAction.MemberShares, ev)
            {
                Total = (uint)shares.Count, ConquerPoints = own.ConquerPoints, Silver = own.Silver,
            };
            int size = ev == AwardEvent.CaptureTheFlag ? CaptureTheFlagSharesPage : PageSize;
            msg.Shares.AddRange(OnPage(shares, page, size)
                .Select(s => (s.Exploits, s.ConquerPoints, s.Silver, s.CharacterID, s.Name)));
            return msg;
        }

        // The guild's exploits in the running event, most first; the reader's own at 18
        public static MsgSelfSynMemAwardRank ExploitsPacket(uint characterID, AwardEvent ev, uint page)
        {
            var guildID = Guilds.Of(characterID)?.Guild.ID ?? 0;
            List<(string Name, uint Exploits)> list;
            uint own;
            lock (Sync)
            {
                var tally = Tallies.GetValueOrDefault(ev) ?? new Dictionary<uint, Tally>();
                own = tally.GetValueOrDefault(characterID)?.Exploits ?? 0;
                list = tally.Values.Where(t => t.GuildID == guildID && t.Exploits > 0)
                    .OrderByDescending(t => t.Exploits).ThenBy(t => t.Name, StringComparer.Ordinal)
                    .Select(t => (t.Name, t.Exploits)).ToList();
            }
            var msg = new MsgSelfSynMemAwardRank(AwardAction.Exploits, ev) { Total = (uint)list.Count, ConquerPoints = own };
            msg.Exploits.AddRange(OnPage(list, page, PageSize));
            return msg;
        }

        public static MsgSelfSynMemAwardRank OwnExploitsPacket(uint exploits) =>
            new MsgSelfSynMemAwardRank(AwardAction.OwnExploits, AwardEvent.CaptureTheFlag) { ConquerPoints = exploits };

        private static IEnumerable<T> OnPage<T>(IEnumerable<T> items, uint page, int size) =>
            items.Skip((int)(Math.Max(page, 1u) - 1) * size).Take(size);

        // ---- The end of an event ----

        // Splits each guild's pool among its members by the exploits they earned in the event
        public static async Task PayOutAsync(AwardEvent ev)
        {
            Dictionary<uint, Tally> tally;
            lock (Sync)
            {
                tally = Tallies.GetValueOrDefault(ev) ?? new Dictionary<uint, Tally>();
                Tallies.Remove(ev);
            }
            foreach (var group in tally.Where(t => t.Value.Exploits > 0).GroupBy(t => t.Value.GuildID))
            {
                var earners = group.OrderByDescending(t => t.Value.Exploits).ThenBy(t => t.Value.Name, StringComparer.Ordinal)
                    .Select(t => (ID: t.Key, t.Value.Name, t.Value.Exploits)).ToList();
                try
                {
                    await PayGuildAsync(group.Key, ev, earners);
                }
                catch (Exception e)
                {
                    LogEvents.For(typeof(GuildRewards), LogEvents.GuildRewardFailed).Error(e,
                        "Paying guild {GuildId}'s {RewardEvent} reward failed", group.Key, ev);
                }
            }
        }

        private static async Task PayGuildAsync(uint guildID, AwardEvent ev, List<(uint ID, string Name, uint Exploits)> earners)
        {
            var split = await Guilds.ChangeAsync(async () =>
            {
                var guild = Guilds.Find(guildID);
                var pool = PoolOf(guildID, ev);
                if (guild == null || (pool.ConquerPoints == 0 && pool.Silver == 0))
                    return null;
                var shares = Split(pool.ConquerPoints, pool.Silver, earners.Select(e => (e.ID, e.Exploits)).ToList());
                uint restCps = pool.ConquerPoints - (uint)shares.Sum(s => (long)s.ConquerPoints);
                ulong restSilver = pool.Silver - shares.Aggregate(0ul, (sum, s) => sum + s.Silver);
                var rows = earners.Zip(shares, (e, s) => new DbGuildRewardShare
                {
                    SyndicateID = guildID, Event = (byte)ev, CharacterID = e.ID, Name = e.Name ?? string.Empty,
                    Exploits = e.Exploits, ConquerPoints = s.ConquerPoints, Silver = s.Silver,
                }).ToList();
                guild.ConquerPointsFund = (uint)Math.Min(uint.MaxValue, (ulong)guild.ConquerPointsFund + restCps);
                guild.Fund += restSilver;
                await GuildRewardsRepository.SaveSplitAsync(guild.ToRow(), (byte)ev, rows);
                lock (Sync)
                {
                    Pools.Remove((guildID, ev));
                    Splits[(guildID, ev)] = rows;
                }
                LogEvents.For(typeof(GuildRewards), LogEvents.GuildRewardSplit).Information(
                    "Guild {Guild} ({GuildId})'s {RewardEvent} reward of {ConquerPoints} CPs and {Silver} silver went to {MemberCount} members; {RestConquerPoints} CPs and {RestSilver} silver back to its funds",
                    guild.Name, guildID, ev, pool.ConquerPoints, pool.Silver, rows.Count, restCps, restSilver);
                return rows;
            });
            if (split == null)
                return;

            // An event can end from a game master's command on that player's packet queue, and
            // paying an online member waits on their queue, so the payments don't hold the caller
            _ = Task.Run(async () =>
            {
                try { await PayMembersAsync(guildID, ev, split); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(GuildRewards), LogEvents.GuildRewardFailed).Error(e,
                        "Paying guild {GuildId}'s {RewardEvent} reward failed", guildID, ev);
                }
            });
        }

        private static async Task PayMembersAsync(uint guildID, AwardEvent ev, List<DbGuildRewardShare> split)
        {
            foreach (var share in split.Where(s => s.ConquerPoints > 0 || s.Silver > 0))
            {
                var (outcome, online, paid) = await CharacterAccess.RunAsync(share.CharacterID,
                    async client =>
                    {
                        bool paid = (share.Silver == 0 || await Progress.ChangeSilverAsync(client, (long)share.Silver, "guild_reward")) &
                            (share.ConquerPoints == 0 || await Progress.ChangeConquerPointsAsync(client, share.ConquerPoints, "guild_reward"));
                        await Talk.NoticeAsync(client,
                            $"You received {share.ConquerPoints} CPs and {share.Silver} silver from your guild's reward for your {share.Exploits} exploits.");
                        return paid;
                    },
                    async saved =>
                    {
                        ulong silver = saved.Silver + share.Silver, cps = (ulong)saved.Jewels + share.ConquerPoints;
                        if (silver > uint.MaxValue || cps > uint.MaxValue)
                            return false;
                        (saved.Silver, saved.Jewels) = ((uint)silver, (uint)cps);
                        await CharactersRepository.SaveAsync(saved);
                        return true;
                    });
                LogEvents.For(typeof(GuildRewards), outcome == CharacterAccess.Outcome.Done && paid
                        ? LogEvents.GuildRewardPaid : LogEvents.GuildRewardFailed)
                    .Information(
                        "{Name} ({CharacterId}) got {ConquerPoints} CPs and {Silver} silver for {Exploits} exploits from guild {GuildId}'s {RewardEvent} reward: {Outcome} {Where}",
                        share.Name, share.CharacterID, share.ConquerPoints, share.Silver, share.Exploits, guildID, ev,
                        outcome, online ? "online" : "offline");
            }
        }

        private static void Unhandled(MsgSelfSynMemAwardRank msg) =>
            LogEvents.For(typeof(GuildRewards), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} event {RewardEvent}", msg.Type, msg.Action, msg.Event);
    }
}

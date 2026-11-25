namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgUserAttrib;

    // Weekly Guild War on map 1038. Players attack the left and right gates (6701, 6702)
    // and the guild pole (6700). Damage on the pole is credited to the attacker's guild;
    // when the pole's life runs out, the guild with the most damage captures it, gates close
    // and refill, and the pole shows the holding guild's name. The guild holding the pole
    // when the war ends wins until the next war and its leader can claim the prize.
    public static class GuildWar
    {
        public const uint MapID = 1038;
        public const uint PoleID = 6700;
        public const uint LeftGateID = 6701;
        public const uint RightGateID = 6702;

        // Closed/open gate meshes from ini/terrainnpc.ini (NpcType24/25 wall-l1/l2, 27/28 wall-r1/r2)
        public const ushort LeftGateClosedLook = 241;
        public const ushort LeftGateOpenLook = 251;
        public const ushort RightGateClosedLook = 271;
        public const ushort RightGateOpenLook = 281;

        public const string DefaultPoleName = "GuildPole";
        public const string LeftGateName = "LeftGate";
        public const string RightGateName = "RightGate";

        public static DayOfWeek StartDay { get; set; } = DayOfWeek.Saturday;
        public static int StartHour { get; set; } = 20;
        public static int StartMinute { get; set; } = 0;
        public static DayOfWeek EndDay { get; set; } = DayOfWeek.Sunday;
        public static int EndHour { get; set; } = 20;
        public static int EndMinute { get; set; } = 0;
        public static uint PoleMaxLife { get; set; } = 20_000_000;
        public static uint GateMaxLife { get; set; } = 10_000_000;
        public static uint PrizeConquerPoints { get; set; } = 3_000;

        private static readonly TimeSpan ScoreboardInterval = TimeSpan.FromSeconds(5);

        private static readonly object Sync = new object();
        private static bool IsActive;
        private static bool? ManualOverride;
        private static uint WinnerID;
        private static uint HolderID;
        private static bool Claimed;
        private static DateTime? LastWonAt;
        private static uint PoleLife = 20_000_000;
        private static uint LeftGateLife = 10_000_000;
        private static uint RightGateLife = 10_000_000;
        private static bool LeftGateOpen;
        private static bool RightGateOpen;
        private static readonly Dictionary<uint, ulong> Scores = new Dictionary<uint, ulong>();
        private static DateTime NextScoreboardAt = DateTime.MinValue;

        public static bool Active { get { lock (Sync) return IsActive; } }
        public static uint WinnerGuildID { get { lock (Sync) return WinnerID; } }
        public static uint HolderGuildID { get { lock (Sync) return HolderID; } }
        public static bool RewardClaimed { get { lock (Sync) return Claimed; } }
        public static DateTime? WonAt { get { lock (Sync) return LastWonAt; } }

        public static uint RulingGuildID
        {
            get { lock (Sync) return HolderID != 0 ? HolderID : WinnerID; }
        }

        public static string HolderGuildName => Guilds.Find(HolderGuildID)?.Name;
        public static string WinnerGuildName => Guilds.Find(WinnerGuildID)?.Name;

        public static bool IsGateOpen(uint npcID)
        {
            lock (Sync)
                return npcID == LeftGateID ? LeftGateOpen : npcID == RightGateID && RightGateOpen;
        }

        public static bool IsScheduledWindow(DateTime utcNow) =>
            IsScheduledWindow(Clock.Local(utcNow), StartDay, StartHour, StartMinute, EndDay, EndHour, EndMinute);

        public static bool IsScheduledWindow(DateTime localNow, DayOfWeek startDay, int startHour, int startMinute,
            DayOfWeek endDay, int endHour, int endMinute)
        {
            int now = MinuteOfWeek(localNow.DayOfWeek, localNow.Hour, localNow.Minute);
            int start = MinuteOfWeek(startDay, startHour, startMinute);
            int end = MinuteOfWeek(endDay, endHour, endMinute);
            if (start == end)
                return false;
            return start < end ? now >= start && now < end : now >= start || now < end;
        }

        private static int MinuteOfWeek(DayOfWeek day, int hour, int minute) =>
            (int)day * 1440 + Math.Clamp(hour, 0, 23) * 60 + Math.Clamp(minute, 0, 59);

        public static string ScheduleText =>
            $"{StartDay} {StartHour:00}:{StartMinute:00} to {EndDay} {EndHour:00}:{EndMinute:00}";

        public static async Task LoadAsync()
        {
            var row = await GuildWarRepository.LoadAsync();
            lock (Sync)
            {
                WinnerID = row != null && row.WinnerGuildID != 0 && Guilds.Find(row.WinnerGuildID) != null
                    ? row.WinnerGuildID : 0;
                HolderID = row != null && row.HolderGuildID != 0 && Guilds.Find(row.HolderGuildID) != null
                    ? row.HolderGuildID : WinnerID;
                Claimed = row?.RewardClaimed ?? false;
                LastWonAt = row?.WonAt;
                IsActive = false;
                ManualOverride = null;
                PoleLife = PoleMaxLife;
                LeftGateLife = GateMaxLife;
                RightGateLife = GateMaxLife;
                LeftGateOpen = false;
                RightGateOpen = false;
                Scores.Clear();
                NextScoreboardAt = DateTime.MinValue;
            }
            LogEvents.For(typeof(GuildWar), LogEvents.GuildWarLoaded).Information(
                "Loaded Guild War state (winner {WinnerId}, holder {HolderId}, claimed {Claimed})",
                WinnerGuildID, HolderGuildID, RewardClaimed);
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(GuildWar), LogEvents.GuildWarLoopFailed).Error(e, "Guild War loop failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now)
        {
            bool scheduled = IsScheduledWindow(now);
            bool wantActive;
            bool shouldSendScores = false;
            lock (Sync)
            {
                if (ManualOverride.HasValue && ManualOverride.Value == scheduled)
                    ManualOverride = null;
                wantActive = ManualOverride ?? scheduled;
                if (IsActive && wantActive && Scores.Count > 0 && now >= NextScoreboardAt)
                {
                    NextScoreboardAt = now + ScoreboardInterval;
                    shouldSendScores = true;
                }
            }

            if (wantActive && !Active)
                await StartAsync(now, manual: false);
            else if (!wantActive && Active)
                await EndAsync(now, manual: false);
            else if (shouldSendScores)
                await SendScoreboardAsync();
        }

        public static async Task<bool> StartAsync(DateTime now, bool manual = false)
        {
            lock (Sync)
            {
                if (manual)
                    ManualOverride = true;
                if (IsActive)
                    return false;
                IsActive = true;
                PoleLife = PoleMaxLife;
                LeftGateLife = GateMaxLife;
                RightGateLife = GateMaxLife;
                LeftGateOpen = false;
                RightGateOpen = false;
                Scores.Clear();
                NextScoreboardAt = now + ScoreboardInterval;
            }

            await RefreshAllStructuresAsync();
            await BroadcastWorldAsync("Guild War has begun! Speak with the Guild Controller in Twin City to enter the Guild War map.");
            LogEvents.For(typeof(GuildWar), LogEvents.GuildWarStarted).Information(
                "Guild War started ({Trigger}, holder {HolderId})", manual ? "manual" : "schedule", HolderGuildID);
            return true;
        }

        public static async Task<bool> EndAsync(DateTime now, bool manual = false)
        {
            uint oldWinner, newWinner;
            DbGuildWar save;
            lock (Sync)
            {
                if (manual)
                    ManualOverride = false;
                if (!IsActive)
                    return false;
                IsActive = false;
                oldWinner = WinnerID;
                if (HolderID == 0 && Scores.Count > 0)
                    HolderID = Scores.OrderByDescending(x => x.Value).First().Key;
                WinnerID = HolderID;
                newWinner = WinnerID;
                Claimed = false;
                LastWonAt = newWinner != 0 ? now : null;
                PoleLife = PoleMaxLife;
                LeftGateLife = GateMaxLife;
                RightGateLife = GateMaxLife;
                LeftGateOpen = false;
                RightGateOpen = false;
                Scores.Clear();
                save = SnapshotLocked();
            }

            await GuildWarRepository.SaveAsync(save);
            await RefreshAllStructuresAsync();
            await ClearScoreboardAsync();
            await RefreshGuildFlagsAsync(oldWinner, newWinner);

            var winnerName = Guilds.Find(newWinner)?.Name;
            await BroadcastWorldAsync(winnerName != null
                ? $"Guild War has ended! {winnerName} is victorious and rules the Guild Arena!"
                : "Guild War has ended without a victor.");
            LogEvents.For(typeof(GuildWar), LogEvents.GuildWarEnded).Information(
                "Guild War ended ({Trigger}, winner {WinnerId} {WinnerName})",
                manual ? "manual" : "schedule", newWinner, winnerName ?? "none");
            return true;
        }

        public static async Task OnGuildDisbandedAsync(uint guildID)
        {
            bool changed = false;
            DbGuildWar save = null;
            lock (Sync)
            {
                Scores.Remove(guildID);
                if (WinnerID == guildID) { WinnerID = 0; Claimed = false; changed = true; }
                if (HolderID == guildID) { HolderID = 0; changed = true; }
                if (changed)
                    save = SnapshotLocked();
            }
            if (!changed)
                return;
            await GuildWarRepository.SaveAsync(save);
            await RefreshStructureAsync(PoleID);
        }

        public static Npc FindTarget(Client client, uint npcID) =>
            client.Character != null && client.Character.MapID == MapID &&
            Kernel.Npcs.Find(MapID, npcID) is Npc npc && npc.IsGuildWarTarget ? npc :
            CaptureTheFlag.FindBase(client, npcID);

        public static string Problem(Client client, Npc target)
        {
            if (target == null || !target.IsGuildWarTarget)
                return "unknown_target";
            if (target.IsFlagBase)
                return CaptureTheFlag.Problem(client, target);
            lock (Sync)
            {
                if (!IsActive)
                    return "war_not_active";
                if (target.Role == Npc.PoleRole)
                {
                    var membership = Guilds.Of(client.ID);
                    if (membership == null)
                        return "no_guild";
                    if (membership.Guild.ID == HolderID)
                        return "own_pole";
                }
                else if (target.Role == Npc.GateRole)
                {
                    if ((target.NpcID == LeftGateID && LeftGateOpen) ||
                        (target.NpcID == RightGateID && RightGateOpen))
                        return "gate_open";
                }
            }
            return null;
        }

        public static IEnumerable<Npc> InView(Client client)
        {
            var character = client.Character;
            foreach (var flagBase in CaptureTheFlag.BasesInView(client))
                yield return flagBase;
            if (character == null || character.MapID != MapID)
                yield break;
            foreach (var npc in Kernel.Npcs.InView(MapID, character.X, character.Y))
                if (npc.IsGuildWarTarget && Problem(client, npc) == null)
                    yield return npc;
        }

        public static uint LifeOf(Npc npc)
        {
            if (npc.IsFlagBase)
                return CaptureTheFlag.BaseLife;
            lock (Sync)
            {
                return npc.NpcID switch
                {
                    PoleID => PoleLife,
                    LeftGateID => LeftGateLife,
                    RightGateID => RightGateLife,
                    _ => npc.Life,
                };
            }
        }

        public static MsgNpcInfoEx SpawnPacket(Npc npc)
        {
            if (npc.IsFlagBase)
                return CaptureTheFlag.BasePacket(npc);
            uint maxLife, life;
            ushort lookface;
            string name;
            lock (Sync)
            {
                if (npc.NpcID == PoleID)
                {
                    maxLife = PoleMaxLife;
                    life = PoleLife;
                    lookface = npc.Lookface;
                    name = Guilds.Find(HolderID)?.Name ?? DefaultPoleName;
                }
                else if (npc.NpcID == LeftGateID)
                {
                    maxLife = LeftGateOpen ? 0u : GateMaxLife;
                    life = LeftGateOpen ? 0u : LeftGateLife;
                    lookface = LeftGateOpen ? LeftGateOpenLook : LeftGateClosedLook;
                    name = LeftGateName;
                }
                else if (npc.NpcID == RightGateID)
                {
                    maxLife = RightGateOpen ? 0u : GateMaxLife;
                    life = RightGateOpen ? 0u : RightGateLife;
                    lookface = RightGateOpen ? RightGateOpenLook : RightGateClosedLook;
                    name = RightGateName;
                }
                else
                {
                    maxLife = npc.Life;
                    life = npc.Life;
                    lookface = npc.Lookface;
                    name = null;
                }
            }
            return new MsgNpcInfoEx(npc, maxLife, life, lookface, name);
        }

        public static async Task AfterHitAsync(Client client, Npc target, uint damage, string kind)
        {
            if (damage == 0 || target == null || !target.IsGuildWarTarget)
                return;
            if (target.IsFlagBase)
            {
                await CaptureTheFlag.AfterHitAsync(client, target, damage);
                return;
            }

            if (target.Role == Npc.GateRole)
            {
                await DamageGateAsync(client, target, damage, kind);
                return;
            }
            if (target.Role == Npc.PoleRole)
                await DamagePoleAsync(client, target, damage, kind);
        }

        private static async Task DamageGateAsync(Client client, Npc gate, uint damage, string kind)
        {
            uint remaining;
            bool opened = false;
            lock (Sync)
            {
                if (!IsActive)
                    return;
                if (gate.NpcID == LeftGateID)
                {
                    if (LeftGateOpen)
                        return;
                    if (damage >= LeftGateLife) { LeftGateLife = 0; LeftGateOpen = true; opened = true; }
                    else LeftGateLife -= damage;
                    remaining = LeftGateLife;
                }
                else if (gate.NpcID == RightGateID)
                {
                    if (RightGateOpen)
                        return;
                    if (damage >= RightGateLife) { RightGateLife = 0; RightGateOpen = true; opened = true; }
                    else RightGateLife -= damage;
                    remaining = RightGateLife;
                }
                else
                {
                    return;
                }
            }

            LogEvents.For(typeof(GuildWar), LogEvents.CombatHit).Debug(
                "{Name} hit gate {NpcId} for {Damage}; {Life} left ({Kind})",
                client.Character?.Name, gate.NpcID, damage, remaining, kind);

            if (opened)
            {
                await SendToViewAsync(gate, SpawnPacket(gate));
                LogEvents.For(typeof(GuildWar), LogEvents.GuildWarGateChanged).Information(
                    "Gate {NpcId} broken open by {Name}", gate.NpcID, client.Character?.Name);
            }
            else
            {
                await SendToViewAsync(gate, new MsgUserAttrib(gate.NpcID, AttributeType.Health, remaining));
            }
        }

        private static async Task DamagePoleAsync(Client client, Npc pole, uint damage, string kind)
        {
            var membership = Guilds.Of(client.ID);
            if (membership == null)
                return;

            uint remaining;
            bool captured = false;
            uint oldHolder = 0, newHolder = 0;
            DbGuildWar save = null;
            lock (Sync)
            {
                if (!IsActive || membership.Guild.ID == HolderID)
                    return;
                uint dealt = Math.Min(damage, PoleLife);
                Scores[membership.Guild.ID] = Scores.GetValueOrDefault(membership.Guild.ID) + dealt;
                if (dealt < PoleLife)
                {
                    PoleLife -= dealt;
                    remaining = PoleLife;
                }
                else
                {
                    captured = true;
                    oldHolder = HolderID;
                    newHolder = Scores.OrderByDescending(x => x.Value).First().Key;
                    HolderID = newHolder;
                    PoleLife = PoleMaxLife;
                    LeftGateLife = GateMaxLife;
                    RightGateLife = GateMaxLife;
                    LeftGateOpen = false;
                    RightGateOpen = false;
                    Scores.Clear();
                    remaining = PoleLife;
                    save = SnapshotLocked();
                }
            }

            LogEvents.For(typeof(GuildWar), LogEvents.CombatHit).Debug(
                "{Name} ({Guild}) hit pole {NpcId} for {Damage}; {Life} left ({Kind})",
                client.Character?.Name, membership.Guild.Name, pole.NpcID, damage, remaining, kind);

            if (!captured)
            {
                await SendToViewAsync(pole, new MsgUserAttrib(pole.NpcID, AttributeType.Health, remaining));
                return;
            }

            await GuildWarRepository.SaveAsync(save);
            await RefreshAllStructuresAsync();
            await ClearScoreboardAsync();
            await RefreshGuildFlagsAsync(oldHolder, newHolder);

            var winnerName = Guilds.Find(newHolder)?.Name ?? "Unknown";
            await BroadcastWorldAsync($"{winnerName} has captured the Guild Pole!");
            LogEvents.For(typeof(GuildWar), LogEvents.GuildWarCaptured).Information(
                "Guild {GuildName} ({GuildId}) captured the pole", winnerName, newHolder);
        }

        public static async Task<bool> ToggleGateAsync(uint gateID, string actor)
        {
            bool open;
            lock (Sync)
            {
                if (gateID == LeftGateID)
                {
                    LeftGateOpen = !LeftGateOpen;
                    if (!LeftGateOpen && LeftGateLife == 0)
                        LeftGateLife = GateMaxLife;
                    open = LeftGateOpen;
                }
                else if (gateID == RightGateID)
                {
                    RightGateOpen = !RightGateOpen;
                    if (!RightGateOpen && RightGateLife == 0)
                        RightGateLife = GateMaxLife;
                    open = RightGateOpen;
                }
                else
                {
                    return false;
                }
            }

            await RefreshStructureAsync(gateID);
            LogEvents.For(typeof(GuildWar), LogEvents.GuildWarGateChanged).Information(
                "Gate {NpcId} {State} by {Name}", gateID, open ? "opened" : "closed", actor);
            return open;
        }

        public static async Task<string> ClaimPrizeAsync(Client client)
        {
            var membership = Guilds.Of(client.ID);
            DbGuildWar save;
            lock (Sync)
            {
                if (IsActive)
                    return "war_active";
                if (WinnerID == 0 || membership?.Guild.ID != WinnerID || membership.Member.Rank != GuildRank.Leader)
                    return "not_winner";
                if (Claimed)
                    return "already_claimed";
                Claimed = true;
                save = SnapshotLocked();
            }

            if (!await Progress.ChangeConquerPointsAsync(client, PrizeConquerPoints, "guild_war_prize"))
            {
                lock (Sync) Claimed = false;
                return "cps_overflow";
            }
            await GuildWarRepository.SaveAsync(save);
            LogEvents.For(typeof(GuildWar), LogEvents.GuildWarPrizeClaimed).Information(
                "{Name} claimed the Guild War prize ({Amount} CPs) for guild {Guild}",
                client.Character?.Name, PrizeConquerPoints, membership.Guild.Name);
            return null;
        }

        public static ulong StatusOf(Client client)
        {
            if (client.Character == null)
                return 0;
            uint ruling = RulingGuildID;
            if (ruling == 0)
                return 0;
            var membership = Guilds.Of(client.ID);
            if (membership == null || membership.Guild.ID != ruling)
                return 0;
            return membership.Member.Rank switch
            {
                GuildRank.Leader => StatusFlags.TopGuildLeader,
                GuildRank.DeputyLeader => StatusFlags.TopDeputyLeader,
                _ => 0,
            };
        }

        public static IReadOnlyList<(uint GuildID, string Name, ulong Score)> TopScores(int limit = 5)
        {
            lock (Sync)
            {
                return Scores
                    .OrderByDescending(x => x.Value)
                    .Take(limit)
                    .Select(x => (x.Key, Guilds.Find(x.Key)?.Name ?? $"Guild#{x.Key}", x.Value))
                    .ToList();
            }
        }

        private static DbGuildWar SnapshotLocked() => new DbGuildWar
        {
            ID = GuildWarRepository.RowID,
            WinnerGuildID = WinnerID,
            HolderGuildID = HolderID,
            RewardClaimed = Claimed,
            WonAt = LastWonAt,
        };

        private static async Task SendScoreboardAsync()
        {
            var top = TopScores(5);
            if (top.Count == 0 || Kernel.World == null || !Kernel.World.TryGet(MapID, out var map))
                return;
            var players = map.All();
            if (players.Count == 0)
                return;
            for (int i = 0; i < top.Count; i++)
            {
                var channel = i == 0 ? MsgTalk.TalkChannel.GuildWarRight1 : MsgTalk.TalkChannel.GuildWarRight2;
                var line = $"No.{i + 1}: {top[i].Name} ({top[i].Score})";
                var msg = new MsgTalk(0, channel, line);
                foreach (var player in players)
                    await player.SendAsync(msg);
            }
        }

        private static async Task ClearScoreboardAsync()
        {
            if (Kernel.World == null || !Kernel.World.TryGet(MapID, out var map))
                return;
            var clear = new MsgTalk(0, MsgTalk.TalkChannel.GuildWarRight1, string.Empty);
            foreach (var player in map.All())
                await player.SendAsync(clear);
        }

        private static async Task RefreshAllStructuresAsync()
        {
            await RefreshStructureAsync(PoleID);
            await RefreshStructureAsync(LeftGateID);
            await RefreshStructureAsync(RightGateID);
        }

        private static async Task RefreshStructureAsync(uint npcID)
        {
            var npc = Kernel.Npcs?.Find(MapID, npcID);
            if (npc == null)
                return;
            await SendToViewAsync(npc, SpawnPacket(npc));
        }

        private static async Task SendToViewAsync(Npc npc, IPacket packet)
        {
            if (Kernel.World == null || !Kernel.World.TryGet(MapID, out var map))
                return;
            foreach (var player in map.InView(npc.X, npc.Y))
                await player.SendAsync(packet);
        }

        private static async Task BroadcastWorldAsync(string text)
        {
            var message = new MsgTalk(0, MsgTalk.TalkChannel.Center, text);
            foreach (var player in Kernel.Players.Values)
                if (player.Character != null)
                    await player.SendAsync(message);
        }

        private static async Task RefreshGuildFlagsAsync(uint oldGuildID, uint newGuildID)
        {
            if (oldGuildID == newGuildID)
                return;
            foreach (var guildID in new[] { oldGuildID, newGuildID })
            {
                var guild = Guilds.Find(guildID);
                if (guild == null)
                    continue;
                foreach (var member in guild.Members.Values)
                {
                    if (member.Rank is GuildRank.Leader or GuildRank.DeputyLeader &&
                        Kernel.Players.TryGet(member.CharacterID, out var player) &&
                        player.Character != null)
                        await Pking.SendStatusAsync(player);
                }
            }
        }
    }
}

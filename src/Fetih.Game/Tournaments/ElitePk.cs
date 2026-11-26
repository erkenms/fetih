namespace Fetih.Game.Tournaments
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Events;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgElitePKArenic;

    // The weekly Elite PK Tournament, one per level group (under 100, 100-119, 120-129, 130+),
    // timed by the client's Activity.ini "Elite PK" entries: on Friday sign-up opens at 19:55
    // and the groups start from 20:00 two minutes apart. Players sign up at the Elite PK Envoy
    // and wait in their group's waiting room; quitting it forfeits. Knock-out rounds of 1-on-1
    // matches follow, each in its own arena: 3 minutes, a kill wins, else the higher damage
    // score, then battle power, then level. The next round starts once the round is over. From
    // the last eight on, matches last 5 minutes, and the semifinal losers fight for third.
    // Every match gives both fighters an Elite PK EXP Pack and 1,000 Honor Points. The final
    // eight claim a pack (and, below 130, some of a level's EXP) at the Envoy until the
    // group's next tournament opens.
    public static class ElitePk
    {
        public const string EventName = "Elite PK";
        public const uint EnvoyID = 7879;
        public const uint WaitingMapID = 2068;
        public const uint ArenaMapID = Arena.Qualifier.MapID;
        public const int Groups = 4;

        public const uint ExpPackType = 720713;
        public const uint HonorPerMatch = 1000;
        // Refined, Unique, Elite, Super by group; Champion, 2nd, 3rd, Top 8 four apart
        public const uint PackBase = 720714;

        public static TimeSpan KnockoutTime { get; set; } = TimeSpan.FromMinutes(3);
        public static TimeSpan FinalTime { get; set; } = TimeSpan.FromMinutes(5);
        public static TimeSpan CountdownTime { get; set; } = TimeSpan.FromSeconds(10);
        public static TimeSpan RoundBreak { get; set; } = TimeSpan.FromSeconds(15);
        public static TimeSpan LeaveDelay { get; set; } = TimeSpan.FromSeconds(3);

        public static readonly string[] GroupNames = { "Under 100", "Level 100-119", "Level 120-129", "Level 130+" };

        // Percent of the current level's EXP each rank claims, by group; the 130+ group gets none
        private static readonly int[] ExpPercentByRank = { 0, 100, 50, 30, 15 };

        public static uint PackOf(int group, int rank) => PackBase + (uint)group + 4u * (uint)(rank - 1);

        public static int GroupOf(int level) => level < 100 ? 0 : level < 120 ? 1 : level < 130 ? 2 : 3;

        public static int ExpPercentOf(int group, int rank) =>
            group == 3 || rank < 1 || rank > 4 ? 0 : ExpPercentByRank[rank];

        public enum Phase { Idle, SignUp, Running }

        private sealed class Match
        {
            public uint A, B;
            public bool ThirdPlace;
            public GameMap<Client> Map;
            public DateTime FightAt, EndsAt;
            public uint ScoreA, ScoreB;
            public bool Fighting, Over, Moved;
            public uint Winner;
            public DateTime OverAt;

            public bool Has(uint id) => id == this.A || id == this.B;
            public uint Opponent(uint id) => id == this.A ? this.B : this.A;
        }

        private sealed class Tournament
        {
            public int Group;
            public Phase Phase;
            public GameMap<Client> WaitingRoom;
            public readonly HashSet<uint> Entrants = new HashSet<uint>();
            public readonly Dictionary<uint, MapPoint> Returns = new Dictionary<uint, MapPoint>();
            public EliteBracket Bracket;
            public readonly List<Match> Matches = new List<Match>();
            public DateTime NextRoundAt;
            public TimeSpan MatchTime;
        }

        private static readonly object Sync = new object();
        private static readonly Tournament[] Tournaments = Enumerable.Range(0, Groups)
            .Select(g => new Tournament { Group = g }).ToArray();
        private static readonly Dictionary<uint, DbElitePkReward> Rewards = new Dictionary<uint, DbElitePkReward>();
        private static readonly Random Random = new Random();

        public static void Subscribe() => EventScheduler.Subscribe(EventName, OnEdgeAsync);

        public static async Task LoadAsync()
        {
            var rows = await ElitePkRepository.LoadAsync();
            lock (Sync)
            {
                Rewards.Clear();
                foreach (var row in rows)
                    Rewards[row.CharacterID] = row;
            }
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkLoaded).Information(
                "Loaded {RewardCount} Elite PK placements", rows.Count);
        }

        public static Phase PhaseOf(int group)
        {
            lock (Sync)
                return Tournaments[group].Phase;
        }

        public static DbElitePkReward RewardOf(uint characterID)
        {
            lock (Sync)
                return Rewards.GetValueOrDefault(characterID);
        }

        // The Activity.ini entry of a group, by its level range
        public static ScheduledEvent EventOf(int group) =>
            EventScheduler.Schedule.Named(EventName).FirstOrDefault(e => GroupOf(e.LevelMin) == group);

        private static Task OnEdgeAsync(ScheduledEvent ev, EventWindow window, EventEdge edge, DateTime now)
        {
            int group = GroupOf(ev.LevelMin);
            return edge switch
            {
                EventEdge.SignUpOpens => OpenAsync(group, now),
                EventEdge.Starts => StartAsync(group, now),
                _ => Task.CompletedTask,
            };
        }

        // Sign-up opens; last week's unclaimed prizes of the group are gone
        public static async Task<bool> OpenAsync(int group, DateTime now)
        {
            lock (Sync)
            {
                var t = Tournaments[group];
                if (t.Phase != Phase.Idle)
                    return false;
                if (Kernel.Maps?.Exists(WaitingMapID) != true || Kernel.Maps?.Exists(ArenaMapID) != true)
                {
                    LogEvents.For(typeof(ElitePk), LogEvents.ElitePkFailed).Error(
                        "No Elite PK maps: {WaitingMapId} or {ArenaMapId} isn't loaded", WaitingMapID, ArenaMapID);
                    return false;
                }
                t.Phase = Phase.SignUp;
                t.WaitingRoom = new GameMap<Client>(WaitingMapID, Kernel.Maps.Get(WaitingMapID));
                t.Entrants.Clear();
                t.Returns.Clear();
                t.Matches.Clear();
                t.Bracket = null;
                foreach (var id in Rewards.Where(r => r.Value.Group == group).Select(r => r.Key).ToList())
                    Rewards.Remove(id);
            }
            await ElitePkRepository.ClearGroupAsync((byte)group);
            await Talk.BroadcastAsync($"The Elite PK Tournament ({GroupNames[group]}) is open! Sign up with the Elite PK Envoy in Twin City before it starts.");
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkOpened).Information(
                "Elite PK sign-up opened for group {Group} ({GroupName})", group, GroupNames[group]);
            return true;
        }

        // At the Envoy; null when signed up, else why not
        public static async Task<string> SignUpAsync(Client client)
        {
            var character = client.Character;
            int group = GroupOf(character.Level);
            Tournament t = Tournaments[group];
            string reason;
            string otherEvent = EventHelpers.CheckOtherEvents(client,
                (GameEvent.Qualifier, "qualifier"),
                (GameEvent.TeamPk, "team_pk"));
            lock (Sync)
            {
                reason =
                    t.Phase == Phase.Running ? "started" :
                    t.Phase != Phase.SignUp ? "closed" :
                    Tournaments.Any(x => x.Entrants.Contains(client.ID)) ? "signed_up" :
                    client.Life.Dead ? "dead" :
                    otherEvent;
                if (reason == null)
                {
                    t.Entrants.Add(client.ID);
                    t.Returns[client.ID] = new MapPoint(character.MapID, character.X, character.Y);
                }
            }
            if (reason != null)
            {
                Reject(client, "sign_up", reason);
                return reason;
            }
            await Riding.DismountAsync(client, "elite_pk");
            var (x, y) = WaitingSpot(t.WaitingRoom.Grid);
            if (!await Movement.TeleportAsync(client, t.WaitingRoom, x, y))
            {
                lock (Sync)
                    t.Entrants.Remove(client.ID);
                Reject(client, "sign_up", "teleport");
                return "teleport";
            }
            await Talk.NoticeAsync(client, "You are in the waiting area for the Elite PK Tournament.");
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkSignedUp).Information(
                "{Name} signed up for the Elite PK Tournament, group {Group} (level {Level})",
                character.Name, group, character.Level);
            return null;
        }

        public static async Task<bool> StartAsync(int group, DateTime now)
        {
            List<uint> sendHome = null;
            int count;
            lock (Sync)
            {
                var t = Tournaments[group];
                if (t.Phase != Phase.SignUp)
                    return false;
                // Only those still waiting take part
                t.Entrants.RemoveWhere(id => !Kernel.Players.TryGet(id, out var p) || p.Map != t.WaitingRoom);
                count = t.Entrants.Count;
                if (count < 2)
                {
                    sendHome = t.Entrants.ToList();
                    t.Phase = Phase.Idle;
                }
                else
                {
                    t.Phase = Phase.Running;
                    t.Bracket = new EliteBracket(t.Entrants);
                    t.NextRoundAt = now;
                }
            }
            if (sendHome != null)
            {
                foreach (var id in sendHome)
                    if (Kernel.Players.TryGet(id, out var player))
                        Kernel.Post(player, async () =>
                        {
                            await Talk.NoticeAsync(player, "Not enough players signed up. The Elite PK Tournament is cancelled.");
                            await SendHomeAsync(player, group);
                        });
                LogEvents.For(typeof(ElitePk), LogEvents.ElitePkEnded).Information(
                    "Elite PK group {Group} cancelled: {Count} entrants", group, count);
                return false;
            }
            await Talk.BroadcastAsync($"The Elite PK Tournament ({GroupNames[group]}) has started with {count} players!");
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkStarted).Information(
                "Elite PK group {Group} started with {Count} players", group, count);
            return true;
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(ElitePk), LogEvents.ElitePkFailed).Error(e, "Elite PK loop failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now)
        {
            for (int group = 0; group < Groups; group++)
                await TickAsync(Tournaments[group], now);
        }

        private static async Task TickAsync(Tournament t, DateTime now)
        {
            var timedOut = new List<Match>();
            var toMove = new List<Match>();
            EliteRound round = null;
            bool finished = false;
            lock (Sync)
            {
                if (t.Phase != Phase.Running)
                    return;
                foreach (var match in t.Matches)
                {
                    if (!match.Over && !match.Fighting && now >= match.FightAt)
                        match.Fighting = true;
                    if (!match.Over && now >= match.EndsAt)
                        timedOut.Add(match);
                    if (match.Over && !match.Moved && now >= match.OverAt + LeaveDelay)
                    {
                        match.Moved = true;
                        toMove.Add(match);
                    }
                }
            }
            foreach (var match in timedOut)
                await DecideAsync(t, match, WinnerOnTime(match), "time", now);
            foreach (var match in toMove)
                await MoveOutAsync(t, match);

            lock (Sync)
            {
                if (t.Matches.Any(m => !m.Moved))
                    return;
                if (t.Bracket.Finished)
                    finished = true;
                else if (now >= t.NextRoundAt)
                {
                    t.Matches.Clear();
                    // Whoever left the waiting room is out
                    foreach (var id in t.Bracket.Alive.ToList())
                        if (!Kernel.Players.TryGet(id, out var p) || p.Map != t.WaitingRoom)
                            t.Bracket.Withdraw(id);
                    if (t.Bracket.Finished)
                        finished = true;
                    else
                    {
                        round = t.Bracket.NextRound(Random);
                        if (round == null)
                            return;
                        t.MatchTime = round.Stage == EliteStage.Knockout ? KnockoutTime : FinalTime;
                        foreach (var (a, b) in round.Pairs)
                            t.Matches.Add(NewMatch(a, b, false, now, t.MatchTime));
                        if (round.ThirdPlace is (uint x, uint y))
                            t.Matches.Add(NewMatch(x, y, true, now, t.MatchTime));
                    }
                }
            }
            if (finished)
                await FinishAsync(t, now);
            else if (round != null)
                await BeginRoundAsync(t, round);
        }

        private static Match NewMatch(uint a, uint b, bool third, DateTime now, TimeSpan length) => new Match
        {
            A = a, B = b, ThirdPlace = third,
            Map = new GameMap<Client>(ArenaMapID, Kernel.Maps.Get(ArenaMapID)),
            FightAt = now + CountdownTime,
            EndsAt = now + CountdownTime + length,
        };

        private static async Task BeginRoundAsync(Tournament t, EliteRound round)
        {
            List<Match> matches;
            lock (Sync)
                matches = t.Matches.ToList();
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkRound).Information(
                "Elite PK group {Group} round {Round} ({Stage}): {Pairs}, bye {Bye}",
                t.Group, t.Bracket.RoundNumber, round.Stage,
                string.Join(", ", matches.Select(m => $"{m.A}-{m.B}")), round.Bye);

            string stage = round.Stage switch
            {
                EliteStage.Knockout => $"Knock-out round {t.Bracket.RoundNumber}",
                EliteStage.Quarterfinal => "The quarterfinal",
                EliteStage.Semifinal => "The semifinal",
                _ => "The final",
            };
            await GroupNoticeAsync(t, $"{stage} of the Elite PK Tournament begins!");
            if (round.Bye != 0 && Kernel.Players.TryGet(round.Bye, out var lucky))
                await Talk.NoticeAsync(lucky, "You have no opponent this round and go through to the next one.");

            foreach (var match in matches)
                foreach (var id in new[] { match.A, match.B })
                {
                    if (Kernel.Players.TryGet(id, out var fighter))
                        Kernel.Post(fighter, () => EnterAsync(fighter, t, match));
                    else
                        await DecideAsync(t, match, match.Opponent(id), "absent", DateTime.UtcNow);
                }
        }

        // On the fighter's queue
        private static async Task EnterAsync(Client client, Tournament t, Match match)
        {
            if (client.Character == null)
                return;
            lock (Sync)
                if (match.Over)
                    return;
            if (client.Life.Dead)
                await Combat.Death.ReviveHereAsync(client, "elite_pk");
            var (x, y) = Arena.Qualifier.Spot(match.Map.Grid, client.ID == match.A ? -1 : 1);
            if (!await Movement.TeleportAsync(client, match.Map, x, y))
            {
                await DecideAsync(t, match, match.Opponent(client.ID), "enter_failed", DateTime.UtcNow);
                return;
            }
            await RestoreAsync(client);
            await client.SendAsync(new MsgElitePKArenic(ArenicAction.Start) { Seconds = (ushort)t.MatchTime.TotalSeconds });
            await client.SendAsync(ScoreOf(match));
            var opponent = Kernel.Players.TryGet(match.Opponent(client.ID), out var o) ? o.Character?.Name : "?";
            await Talk.NoticeAsync(client, $"Your opponent is {opponent}. The match starts in {CountdownTime.TotalSeconds:0} seconds.");
        }

        // A kill or the clock; the winner gets the bracket's next round
        private static async Task DecideAsync(Tournament t, Match match, uint winner, string reason, DateTime now)
        {
            MsgElitePKScore score;
            lock (Sync)
            {
                if (match.Over)
                    return;
                match.Over = true;
                match.Winner = winner;
                match.OverAt = now;
                t.Bracket.Record(winner, match.Opponent(winner));
                if (!t.Bracket.RoundOpen)
                    t.NextRoundAt = now + LeaveDelay + RoundBreak;
                score = ScoreOf(match);
            }
            uint loser = match.Opponent(winner);
            string winnerName = NameOf(winner), loserName = NameOf(loser);
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkMatchEnded).Information(
                "Elite PK group {Group}: {Winner} beat {Loser} ({Reason}, {ScoreA}-{ScoreB}{Third})",
                t.Group, winnerName, loserName, reason, match.ScoreA, match.ScoreB, match.ThirdPlace ? ", third place" : "");
            foreach (var id in new[] { match.A, match.B })
                if (Kernel.Players.TryGet(id, out var fighter) && fighter.Character != null)
                {
                    await fighter.SendAsync(score);
                    await fighter.SendAsync(new MsgElitePKArenic(ArenicAction.Result) { Won = id == winner });
                    if (reason != "absent")
                        Kernel.Post(fighter, () => RewardMatchAsync(fighter));
                }
            await GroupNoticeAsync(t, $"{winnerName} has defeated {loserName} in the Elite PK Tournament.");
        }

        private static uint WinnerOnTime(Match match)
        {
            if (match.ScoreA != match.ScoreB)
                return match.ScoreA > match.ScoreB ? match.A : match.B;
            var a = Kernel.Players.TryGet(match.A, out var pa) ? pa.Character : null;
            var b = Kernel.Players.TryGet(match.B, out var pb) ? pb.Character : null;
            if (a == null || b == null)
                return a != null ? match.A : match.B;
            return Compare(Attributes.BattlePower(a), a.Level, Attributes.BattlePower(b), b.Level) >= 0 ? match.A : match.B;
        }

        // Battle power, then level; ties go to the first
        public static int Compare(uint powerA, int levelA, uint powerB, int levelB) =>
            powerA != powerB ? powerA.CompareTo(powerB) : levelA.CompareTo(levelB);

        // On the fighter's queue
        private static async Task RewardMatchAsync(Client client)
        {
            if (client.Character == null)
                return;
            Arena.Qualifier.AddHonor(client, HonorPerMatch);
            if (Kernel.ItemTypes.TryGet(ExpPackType, out var pack) && await ItemActions.GiveAsync(client, pack, "elite_pk_match") == null)
                await Talk.NoticeAsync(client, "Your inventory is full; the Elite PK EXP Pack is lost.");
            await Talk.NoticeAsync(client, $"You received {HonorPerMatch} Honor Points in the Elite PK Tournament.");
        }

        private static async Task MoveOutAsync(Tournament t, Match match)
        {
            foreach (var id in new[] { match.A, match.B })
            {
                if (!Kernel.Players.TryGet(id, out var fighter))
                    continue;
                bool stays;
                lock (Sync)
                    stays = !t.Bracket.Finished &&
                        (id == match.Winner && !match.ThirdPlace || t.Bracket.AwaitsThirdPlace(id));
                Kernel.Post(fighter, async () =>
                {
                    if (fighter.Character == null || fighter.Map != match.Map)
                        return;
                    if (fighter.Life.Dead)
                        await Combat.Death.ReviveHereAsync(fighter, "elite_pk");
                    await RestoreAsync(fighter);
                    await fighter.SendAsync(new MsgElitePKArenic(ArenicAction.Leave));
                    if (stays)
                    {
                        var (x, y) = WaitingSpot(t.WaitingRoom.Grid);
                        if (await Movement.TeleportAsync(fighter, t.WaitingRoom, x, y))
                            return;
                    }
                    await SendHomeAsync(fighter, t.Group);
                });
            }
        }

        private static async Task FinishAsync(Tournament t, DateTime now)
        {
            IReadOnlyList<(uint ID, int Rank)> placements;
            List<uint> waiting;
            lock (Sync)
            {
                if (t.Phase != Phase.Running)
                    return;
                placements = t.Bracket.Placements();
                waiting = t.Entrants.ToList();
                t.Phase = Phase.Idle;
                t.Matches.Clear();
                foreach (var (id, rank) in placements)
                    Rewards[id] = new DbElitePkReward { CharacterID = id, Group = (byte)t.Group, Rank = (byte)rank, Name = NameOf(id) };
            }
            var rows = placements.Select(p => new DbElitePkReward
            {
                CharacterID = p.ID, Group = (byte)t.Group, Rank = (byte)p.Rank, Name = NameOf(p.ID),
            }).ToList();
            await ElitePkRepository.ReplaceGroupAsync((byte)t.Group, rows);

            foreach (var id in waiting)
                if (Kernel.Players.TryGet(id, out var player))
                    Kernel.Post(player, async () =>
                    {
                        if (player.Map == t.WaitingRoom)
                            await SendHomeAsync(player, t.Group);
                    });

            var champion = placements.FirstOrDefault(p => p.Rank == EliteBracket.Champion);
            await Talk.BroadcastAsync(champion.ID != 0
                ? $"{NameOf(champion.ID)} is the champion of the Elite PK Tournament ({GroupNames[t.Group]})! The final eight can claim their prizes from the Elite PK Envoy."
                : $"The Elite PK Tournament ({GroupNames[t.Group]}) is over.");
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkEnded).Information(
                "Elite PK group {Group} ended: {Placements}", t.Group,
                string.Join(", ", placements.Select(p => $"{p.Rank}:{NameOf(p.ID)}")));
        }

        // At the Envoy; null when claimed, else why not
        public static async Task<string> ClaimAsync(Client client)
        {
            DbElitePkReward reward;
            string reason;
            lock (Sync)
            {
                reward = Rewards.GetValueOrDefault(client.ID);
                reason =
                    reward == null ? "no_reward" :
                    reward.Claimed ? "claimed" :
                    client.Inventory == null || client.Inventory.FreeSlots == 0 ? "inventory_full" :
                    null;
                if (reason == null)
                    reward.Claimed = true;
            }
            if (reason != null)
            {
                Reject(client, "claim", reason);
                return reason;
            }
            uint packType = PackOf(reward.Group, reward.Rank);
            if (!Kernel.ItemTypes.TryGet(packType, out var type) ||
                await ItemActions.GiveAsync(client, type, "elite_pk_prize") == null)
            {
                lock (Sync)
                    reward.Claimed = false;
                Reject(client, "claim", "no_item");
                return "no_item";
            }
            await ElitePkRepository.SetClaimedAsync(client.ID, true);
            int percent = ExpPercentOf(reward.Group, reward.Rank);
            var character = client.Character;
            if (percent > 0 && Kernel.Experience.TryGetRequired(character.Level, out var needed) && needed > 0)
                await Progress.AwardExperienceAsync(client, needed * (ulong)percent / 100, "elite_pk_prize");
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkRewardClaimed).Information(
                "{Name} claimed the Elite PK prize: group {Group}, rank {Rank}, pack {TypeId}, {Percent}% EXP",
                character.Name, reward.Group, reward.Rank, packType, percent);
            return null;
        }

        // Combat asks before a hit on a player: null when the attacker isn't in an Elite PK
        // arena, else whether the two fight each other now
        public static bool? CanFight(Client attacker, Client target)
        {
            if (attacker.Map?.Id != ArenaMapID)
                return null;
            lock (Sync)
            {
                var match = MatchOn(attacker.Map);
                if (match == null)
                    return null;
                if (!match.Fighting && !match.Over && DateTime.UtcNow >= match.FightAt)
                    match.Fighting = true;
                return match.Fighting && !match.Over && match.Has(attacker.ID) && match.Has(target.ID) &&
                    target.Map == attacker.Map;
            }
        }

        // On the target's queue, after the damage was taken
        public static async Task OnHitAsync(Client attacker, Client target, uint damage)
        {
            if (damage == 0 || attacker.Map?.Id != ArenaMapID)
                return;
            MsgElitePKScore score;
            lock (Sync)
            {
                var match = MatchOn(attacker.Map);
                if (match == null || !match.Fighting || match.Over || !match.Has(target.ID))
                    return;
                if (attacker.ID == match.A)
                    match.ScoreA += damage;
                else
                    match.ScoreB += damage;
                score = ScoreOf(match);
            }
            await attacker.SendAsync(score);
            await target.SendAsync(score);
        }

        // True if the kill decided an Elite PK match: then it's no PK
        public static async Task<bool> OnKillAsync(Client killer, Client victim)
        {
            if (killer.Map?.Id != ArenaMapID)
                return false;
            Tournament t;
            Match match;
            lock (Sync)
            {
                match = MatchOn(killer.Map);
                t = match == null ? null : Tournaments.First(x => x.Matches.Contains(match));
            }
            if (match == null || !match.Has(victim.ID))
                return false;
            await DecideAsync(t, match, killer.ID, "killed", DateTime.UtcNow);
            return true;
        }

        // Logout: a fighter loses the match, a waiting player is out
        public static async Task LeaveAsync(Client client, string reason)
        {
            Tournament t = null;
            Match match = null;
            lock (Sync)
            {
                foreach (var x in Tournaments)
                {
                    if (x.Phase == Phase.Idle || !x.Entrants.Contains(client.ID))
                        continue;
                    t = x;
                    match = x.Matches.FirstOrDefault(m => !m.Over && m.Has(client.ID));
                    if (match == null)
                    {
                        x.Entrants.Remove(client.ID);
                        x.Bracket?.Withdraw(client.ID);
                    }
                }
            }
            if (t == null)
                return;
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkLeft).Information(
                "{Name} left the Elite PK Tournament, group {Group} ({Reason})", client.Character?.Name, t.Group, reason);
            if (match != null)
                await DecideAsync(t, match, match.Opponent(client.ID), reason, DateTime.UtcNow);
        }

        public static bool IsTakingPart(Client client)
        {
            lock (Sync)
                return Tournaments.Any(t => t.Phase != Phase.Idle && t.Entrants.Contains(client.ID) &&
                    (t.Bracket == null || t.Bracket.Alive.Contains(client.ID)));
        }

        public static string StatusText()
        {
            lock (Sync)
                return "Elite PK: " + string.Join("; ", Tournaments.Select(t =>
                    $"{GroupNames[t.Group]} {t.Phase}, {t.Entrants.Count} entrants" +
                    (t.Bracket == null ? "" : $", round {t.Bracket.RoundNumber}, {t.Bracket.Alive.Count} left, {t.Matches.Count(m => !m.Over)} fighting"))) +
                    $". {Rewards.Count(r => !r.Value.Claimed)} prizes unclaimed.";
        }

        private static Match MatchOn(GameMap<Client> map) =>
            Tournaments.SelectMany(t => t.Matches).FirstOrDefault(m => m.Map == map);

        private static MsgElitePKScore ScoreOf(Match match) => new MsgElitePKScore
        {
            IdA = match.A, NameA = NameOf(match.A), DamageA = match.ScoreA,
            IdB = match.B, NameB = NameOf(match.B), DamageB = match.ScoreB,
        };

        private static string NameOf(uint id)
        {
            if (Kernel.Players.TryGet(id, out var p) && p.Character != null)
                return p.Character.Name;
            lock (Sync)
                return Rewards.TryGetValue(id, out var r) ? r.Name : id.ToString();
        }

        // A walkable cell near the middle of the waiting room, spread out a little
        private static (ushort X, ushort Y) WaitingSpot(MapGrid grid) =>
            Arena.Qualifier.Spot(grid, Random.Next(-6, 7));

        private static async Task SendHomeAsync(Client client, int group)
        {
            await client.SendAsync(new MsgElitePKArenic(ArenicAction.Close));
            MapPoint back;
            lock (Sync)
                back = Tournaments[group].Returns.GetValueOrDefault(client.ID);
            await EventHelpers.TeleportHomeAsync(client, back);
        }

        internal static async Task RestoreAsync(Client client)
        {
            var character = client.Character;
            character.HealthPoints = Attributes.MaxHealth(client);
            character.ManaPoints = Attributes.MaxMana(client);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Mana, character.ManaPoints));
        }

        private static async Task GroupNoticeAsync(Tournament t, string text)
        {
            var present = new List<Client>();
            lock (Sync)
                foreach (var id in t.Entrants)
                    if (Kernel.Players.TryGet(id, out var player) && player.Character != null &&
                        (player.Map == t.WaitingRoom || t.Matches.Any(m => m.Map == player.Map)))
                        present.Add(player);
            var message = new MsgTalk(0, MsgTalk.TalkChannel.System, text);
            foreach (var player in present)
                await player.SendAsync(message);
        }

        private static void Reject(Client client, string kind, string reason) =>
            LogEvents.For(typeof(ElitePk), LogEvents.ElitePkRejected).Information(
                "{Name} can't {Kind} in the Elite PK Tournament: {Reason}", client.Character?.Name, kind, reason);
    }
}

namespace Comet.Game.Tournaments
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Events;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // The weekly Guild Contest and Guild PK Tournament (help page "Guild PK Tournament",
    // STR_FACTION_PK_INTRO_*). Activity.ini doesn't list them, so their times are the help
    // page's: the contest on Monday 20:30-22:30, a round-robin round on Tuesday, Wednesday
    // and Thursday 20:30-22:30, the final on Saturday 21:30-23:30. Each opens 5 minutes early,
    // when members may already enter through the Emissaries in Twin City.
    //
    // In the contest (map 2060) guild members kill monsters: a kill is a guild point and an
    // Exploit. Its top 8 guilds play the tournament (GuildPkTable), each match on a copy of map
    // 2071 between two guilds whose members joined over a week ago. A flag stands in the
    // middle: the guild that breaks it holds it, and earns a guild point every second it does.
    // Defeating an enemy or reviving a guild member is an Exploit. The guild with more points
    // when the match ends wins. Leaders and deputies claim Conquer Points from the Awarder of
    // their place: contest prizes until midnight, tournament prizes until next Saturday 20:30.
    public static class GuildPk
    {
        public const string ContestName = "Guild Contest";
        public const string RoundName = "Guild PK Tournament";
        public const string FinalName = "Guild PK Final";

        public const uint ContestMapID = 2060;
        public const uint MatchMapID = 2071;
        public const uint ContestEmissaryID = 7881, PkEmissaryID = 7882;
        // The Awarders of places 1-8 are FirstAwarderID + place - 1
        public const uint FirstAwarderID = 7883;
        public const uint FirstFlagID = 7891;
        public const ushort FlagLook = 1135;
        public const string FlagName = "GuildFlag";

        public const byte ContestPrize = 0, TournamentPrize = 1;

        public static uint FlagLife { get; set; } = 1_000_000;
        public static int MinGuildDays { get; set; } = 7;
        public static uint[] ContestPrizes { get; set; } = { 1000, 800, 600, 500, 400, 300, 200, 100 };
        public static uint[] TournamentPrizes { get; set; } = { 5000, 3000, 2000, 1500, 1000, 800, 600, 400 };
        public static TimeSpan RunTime { get; set; } = TimeSpan.FromHours(2);

        private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RankingInterval = TimeSpan.FromSeconds(10);

        private static EventWindow Window(int hour, int minute) => new EventWindow(
            new TimeSpan(hour, minute - 5, 0), new TimeSpan(hour, minute, 0), new TimeSpan(hour + 2, minute, 0));

        public static IReadOnlyList<ScheduledEvent> Events { get; } = new[]
        {
            new ScheduledEvent(1001, ContestName, new HashSet<DayOfWeek> { DayOfWeek.Monday }, 1, 255,
                new[] { Window(20, 30) }),
            new ScheduledEvent(1002, RoundName,
                new HashSet<DayOfWeek> { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday }, 1, 255,
                new[] { Window(20, 30) }),
            new ScheduledEvent(1003, FinalName, new HashSet<DayOfWeek> { DayOfWeek.Saturday }, 1, 255,
                new[] { Window(21, 30) }),
        };

        public enum Phase { Idle, Open, Running }

        private sealed class Match
        {
            public int Index;
            public GuildPkPairing Pair;
            public GameMap<Client> Map;
            public Npc Flag;
            public uint FlagLeft;
            public uint Holder;
            public long ScoreA, ScoreB;

            public bool Has(uint guildID) => guildID == this.Pair.A.GuildID || guildID == this.Pair.B.GuildID;
        }

        private static readonly object Sync = new object();
        private static readonly Random Random = new Random();

        private static Phase ContestPhase;
        private static DateTime ContestEndsAt;
        private static readonly Dictionary<uint, long> ContestPoints = new Dictionary<uint, long>();

        private static Phase MatchPhase;
        private static bool FinalStage;
        private static int RoundNumber;
        private static DateTime MatchEndsAt;
        private static readonly List<Match> Matches = new List<Match>();
        private static readonly List<GuildPkPairing> Pairs = new List<GuildPkPairing>();

        private static readonly Dictionary<uint, GuildPkEntry> Entries = new Dictionary<uint, GuildPkEntry>();
        private static readonly Dictionary<(uint, byte), DbGuildPkPrize> Prizes = new Dictionary<(uint, byte), DbGuildPkPrize>();
        private static readonly Dictionary<uint, uint> Exploits = new Dictionary<uint, uint>();
        private static readonly HashSet<uint> ExploitsChanged = new HashSet<uint>();
        private static readonly Dictionary<uint, MapPoint> Returns = new Dictionary<uint, MapPoint>();
        private static DateTime NextStatusAt, NextRankingAt;

        public static void Subscribe()
        {
            EventScheduler.Subscribe(ContestName, (ev, w, edge, now) => edge switch
            {
                EventEdge.SignUpOpens => OpenContestAsync(now),
                EventEdge.Starts => StartContestAsync(now),
                _ => EndContestAsync(now),
            });
            foreach (var (name, final) in new[] { (RoundName, false), (FinalName, true) })
                EventScheduler.Subscribe(name, (ev, w, edge, now) => edge switch
                {
                    EventEdge.SignUpOpens => OpenMatchesAsync(final, now),
                    EventEdge.Starts => StartMatchesAsync(now),
                    _ => EndMatchesAsync(now),
                });
        }

        public static async Task LoadAsync()
        {
            var (entries, prizes, exploits) = await GuildPkRepository.LoadAsync();
            lock (Sync)
            {
                Entries.Clear();
                foreach (var row in entries)
                    Entries[row.SyndicateID] = new GuildPkEntry
                    {
                        GuildID = row.SyndicateID, Name = row.Name, Seed = row.Seed, ContestPoints = row.ContestPoints,
                        Played = row.Played, Points = row.Points, Score = row.Score, FinalRank = row.FinalRank,
                    };
                Prizes.Clear();
                foreach (var row in prizes)
                    Prizes[(row.SyndicateID, row.Kind)] = row;
                Exploits.Clear();
                foreach (var row in exploits)
                    Exploits[row.CharacterID] = row.Exploits;
            }
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkLoaded).Information(
                "Loaded the Guild PK Tournament: {EntryCount} qualified guilds, {PrizeCount} prizes",
                entries.Count, prizes.Count);
        }

        public static Phase ContestPhaseNow { get { lock (Sync) return ContestPhase; } }
        public static Phase MatchPhaseNow { get { lock (Sync) return MatchPhase; } }

        public static uint ExploitsOf(uint characterID)
        {
            lock (Sync)
                return Exploits.GetValueOrDefault(characterID);
        }

        public static GuildPkEntry EntryOf(uint guildID)
        {
            lock (Sync)
                return Entries.GetValueOrDefault(guildID);
        }

        // ---- The Guild Contest ----

        public static async Task<bool> OpenContestAsync(DateTime now)
        {
            lock (Sync)
            {
                if (ContestPhase != Phase.Idle)
                    return false;
                if (!Kernel.World.TryGet(ContestMapID, out _))
                {
                    LogEvents.For(typeof(GuildPk), LogEvents.GuildPkFailed).Error(
                        "No Guild Contest map {MapId}", ContestMapID);
                    return false;
                }
                ContestPhase = Phase.Open;
                ContestPoints.Clear();
            }
            await BroadcastAsync("The Guild Contest is about to begin! Guild members can enter it through the Guild Contest Emissary in Twin City.");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkContestOpened).Information("The Guild Contest opened");
            return true;
        }

        public static async Task<bool> StartContestAsync(DateTime now)
        {
            if (ContestPhaseNow == Phase.Idle && !await OpenContestAsync(now))
                return false;
            lock (Sync)
            {
                if (ContestPhase != Phase.Open)
                    return false;
                ContestPhase = Phase.Running;
                ContestEndsAt = now + RunTime;
            }
            await BroadcastAsync("The Guild Contest has begun! Kill the monsters in the contest map to win points for your guild.");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkContestStarted).Information("The Guild Contest started");
            return true;
        }

        // The top 8 qualify for this week's tournament and may claim their prizes until midnight
        public static async Task<bool> EndContestAsync(DateTime now)
        {
            List<(uint GuildID, long Points)> top;
            List<DbGuildPkEntry> rows;
            List<DbGuildPkPrize> prizes;
            lock (Sync)
            {
                if (ContestPhase == Phase.Idle)
                    return false;
                bool ran = ContestPhase == Phase.Running;
                ContestPhase = Phase.Idle;
                if (!ran)
                    top = new List<(uint, long)>();
                else
                    top = GuildPkTable.Top(ContestPoints, GuildPkTable.Qualifiers)
                        .Where(t => Guilds.Find(t.GuildID) != null).ToList();
                rows = top.Select((t, i) => new DbGuildPkEntry
                {
                    SyndicateID = t.GuildID, Name = Guilds.Find(t.GuildID)?.Name ?? string.Empty,
                    Seed = (byte)(i + 1), ContestPoints = t.Points,
                }).ToList();
                if (ran)
                {
                    Entries.Clear();
                    foreach (var row in rows)
                        Entries[row.SyndicateID] = new GuildPkEntry
                        {
                            GuildID = row.SyndicateID, Name = row.Name, Seed = row.Seed, ContestPoints = row.ContestPoints,
                        };
                }
                var expires = ToUtc(Clock.Local(now).Date.AddDays(1));
                prizes = rows.Select(r => new DbGuildPkPrize
                {
                    SyndicateID = r.SyndicateID, Kind = ContestPrize, Rank = r.Seed, Expires = expires,
                }).ToList();
                if (ran)
                {
                    foreach (var key in Prizes.Keys.Where(k => k.Item2 == ContestPrize).ToList())
                        Prizes.Remove(key);
                    foreach (var prize in prizes)
                        Prizes[(prize.SyndicateID, ContestPrize)] = prize;
                }
                else
                    rows = null;
            }
            if (rows != null)
            {
                await GuildPkRepository.ReplaceEntriesAsync(rows);
                await GuildPkRepository.ReplacePrizesAsync(ContestPrize, prizes);
            }
            await SaveExploitsAsync();
            foreach (var player in Kernel.Players.Values.ToList())
                if (player.Character?.MapID == ContestMapID)
                    Kernel.Post(player, () => SendHomeAsync(player));

            await BroadcastAsync(top.Count == 0
                ? "The Guild Contest is over."
                : "The Guild Contest is over! Qualified for the Guild PK Tournament: " +
                  string.Join(", ", top.Select((t, i) => $"{i + 1}. {Guilds.Find(t.GuildID)?.Name}")) + ".");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkContestEnded).Information(
                "The Guild Contest ended: {Ranking}",
                string.Join(", ", top.Select((t, i) => $"{i + 1}:{Guilds.Find(t.GuildID)?.Name}={t.Points}")));
            return true;
        }

        // At the Guild Contest Emissary; null when let in, else why not
        public static async Task<string> EnterContestAsync(Client client)
        {
            var membership = Guilds.Of(client.ID);
            string reason;
            lock (Sync)
                reason =
                    ContestPhase == Phase.Idle ? "closed" :
                    membership == null ? "no_guild" :
                    client.Life.Dead ? "dead" :
                    null;
            if (reason != null || !Kernel.World.TryGet(ContestMapID, out var map))
                return Reject(client, "enter_contest", reason ?? "no_map");
            var character = client.Character;
            var from = new MapPoint(character.MapID, character.X, character.Y);
            await Items.Riding.DismountAsync(client, "guild_contest");
            var (x, y) = Arena.Qualifier.Spot(map.Grid, Random.Next(-4, 5));
            if (!await Movement.TeleportAsync(client, map, x, y))
                return Reject(client, "enter_contest", "teleport");
            lock (Sync)
                if (from.MapID != ContestMapID)
                    Returns[client.ID] = from;
            await client.SendAsync(RankingPacket());
            await client.SendAsync(MsgSynCompete.GuildPoints(GuildPointsOf(membership.Guild.ID)));
            await client.SendAsync(MsgSynCompete.OfExploits(ExploitsOf(client.ID)));
            await Talk.NoticeAsync(client, "You are in the Guild Contest Map.");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkEntered).Information(
                "{Name} of {Guild} entered the Guild Contest", character.Name, membership.Guild.Name);
            return null;
        }

        // A player killed a monster: on the contest map while it runs, a point for the guild
        public static async Task OnMonsterKilledAsync(Client client, uint mapID)
        {
            if (mapID != ContestMapID || client.Character == null)
                return;
            var membership = Guilds.Of(client.ID);
            if (membership == null)
                return;
            long points;
            uint exploits;
            lock (Sync)
            {
                if (ContestPhase != Phase.Running)
                    return;
                points = ContestPoints[membership.Guild.ID] = ContestPoints.GetValueOrDefault(membership.Guild.ID) + 1;
                exploits = AddExploit(client.ID);
            }
            await client.SendAsync(MsgSynCompete.GuildPoints(points));
            await client.SendAsync(MsgSynCompete.OfExploits(exploits));
        }

        private static long GuildPointsOf(uint guildID)
        {
            lock (Sync)
                return ContestPoints.GetValueOrDefault(guildID);
        }

        private static MsgSynCompete RankingPacket()
        {
            var msg = new MsgSynCompete { Action = MsgSynCompete.CompeteAction.Ranking };
            lock (Sync)
            {
                int rank = 1;
                foreach (var (guildID, points) in GuildPkTable.Top(ContestPoints, GuildPkTable.Qualifiers))
                    msg.Entries.Add(((uint)rank++, points, Guilds.Find(guildID)?.Name ?? string.Empty));
            }
            return msg;
        }

        // ---- The tournament's rounds and final ----

        // Pairs the guilds and makes the match maps; members may enter until the matches end
        public static async Task<bool> OpenMatchesAsync(bool final, DateTime now)
        {
            List<Match> matches;
            int round;
            lock (Sync)
            {
                if (MatchPhase != Phase.Idle || Entries.Count == 0)
                    return false;
                if (Kernel.Maps?.Exists(MatchMapID) != true)
                {
                    LogEvents.For(typeof(GuildPk), LogEvents.GuildPkFailed).Error(
                        "No Guild PK Tournament map {MapId}", MatchMapID);
                    return false;
                }
                round = Entries.Values.Max(e => e.Played) + 1;
                if (final ? Entries.Values.Any(e => e.FinalRank > 0) : round > GuildPkTable.Rounds)
                    return false;
                Pairs.Clear();
                Pairs.AddRange(final ? GuildPkTable.Final(Entries.Values) : GuildPkTable.Round(Entries.Values, round));
                Matches.Clear();
                var grid = Kernel.Maps.Get(MatchMapID);
                foreach (var pair in Pairs.Where(p => !p.Walkover))
                {
                    int index = Matches.Count;
                    var (fx, fy) = Arena.Qualifier.Spot(grid, 0);
                    Matches.Add(new Match
                    {
                        Index = index, Pair = pair,
                        Map = new GameMap<Client>(MatchMapID, grid),
                        Flag = new Npc
                        {
                            NpcID = FirstFlagID + (uint)index, Lookface = FlagLook, Role = Npc.PoleRole,
                            MapID = MatchMapID, X = fx, Y = fy, Life = FlagLife, Level = 1,
                        },
                        FlagLeft = FlagLife,
                    });
                }
                FinalStage = final;
                RoundNumber = final ? 0 : round;
                MatchPhase = Phase.Open;
                MatchEndsAt = now + RunTime + TimeSpan.FromMinutes(5);
                matches = Matches.ToList();
            }
            string stage = final ? "final" : $"round {round}";
            await BroadcastAsync($"The Guild PK Tournament {stage} is about to begin! " +
                string.Join("; ", matches.Select(m => $"{m.Pair.A.Name} vs. {m.Pair.B.Name}")) +
                ". Members can enter through the Guild PK Emissary in Twin City.");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkMatchesOpened).Information(
                "Guild PK Tournament {Stage} opened: {Pairs}", stage,
                string.Join(", ", Pairs.Select(p => $"{p.A.Name}-{p.B?.Name ?? "(walkover)"}")));
            return true;
        }

        public static async Task<bool> StartMatchesAsync(DateTime now)
        {
            string stage;
            lock (Sync)
            {
                if (MatchPhase != Phase.Open)
                    return false;
                MatchPhase = Phase.Running;
                MatchEndsAt = now + RunTime;
                NextStatusAt = now;
                stage = FinalStage ? "final" : $"round {RoundNumber}";
            }
            await BroadcastAsync($"The Guild PK Tournament {stage} has begun! Seize the flag to win points for your guild.");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkMatchesStarted).Information(
                "Guild PK Tournament {Stage} started", stage);
            return true;
        }

        // Records the results; after the final the places 1-8 get their prizes
        public static async Task<bool> EndMatchesAsync(DateTime now)
        {
            var results = new List<(Match Match, string Winner)>();
            List<DbGuildPkEntry> rows;
            List<DbGuildPkPrize> prizes = null;
            List<Match> matches;
            bool final;
            string stage;
            lock (Sync)
            {
                if (MatchPhase == Phase.Idle)
                    return false;
                bool ran = MatchPhase == Phase.Running;
                MatchPhase = Phase.Idle;
                final = FinalStage;
                stage = final ? "final" : $"round {RoundNumber}";
                matches = Matches.ToList();
                Matches.Clear();
                if (ran)
                {
                    foreach (var pair in Pairs.Where(p => p.Walkover))
                        if (final)
                            GuildPkTable.RecordFinal(pair, 0, 0);
                        else
                            GuildPkTable.RecordRound(pair, 0, 0);
                    foreach (var match in matches)
                    {
                        string winner;
                        if (final)
                            winner = GuildPkTable.RecordFinal(match.Pair, match.ScoreA, match.ScoreB).Name;
                        else
                        {
                            GuildPkTable.RecordRound(match.Pair, match.ScoreA, match.ScoreB);
                            winner = match.ScoreA == match.ScoreB ? null
                                : match.ScoreA > match.ScoreB ? match.Pair.A.Name : match.Pair.B.Name;
                        }
                        results.Add((match, winner));
                    }
                    if (final)
                    {
                        var expires = ToUtc(NextSaturdayEvening(Clock.Local(now)));
                        prizes = Entries.Values.Where(e => e.FinalRank > 0).Select(e => new DbGuildPkPrize
                        {
                            SyndicateID = e.GuildID, Kind = TournamentPrize, Rank = (byte)e.FinalRank, Expires = expires,
                        }).ToList();
                        foreach (var key in Prizes.Keys.Where(k => k.Item2 == TournamentPrize).ToList())
                            Prizes.Remove(key);
                        foreach (var prize in prizes)
                            Prizes[(prize.SyndicateID, TournamentPrize)] = prize;
                    }
                }
                Pairs.Clear();
                rows = ran ? Entries.Values.Select(ToRow).ToList() : null;
            }
            if (rows != null)
                await GuildPkRepository.SaveEntriesAsync(rows);
            if (prizes != null)
                await GuildPkRepository.ReplacePrizesAsync(TournamentPrize, prizes);
            await SaveExploitsAsync();

            foreach (var match in matches)
                foreach (var player in match.Map.All())
                    Kernel.Post(player, () => SendHomeAsync(player));
            foreach (var (match, winner) in results)
                LogEvents.For(typeof(GuildPk), LogEvents.GuildPkMatchEnded).Information(
                    "Guild PK Tournament {Stage}: {GuildA} {ScoreA} - {ScoreB} {GuildB}, winner {Winner}",
                    stage, match.Pair.A.Name, match.ScoreA, match.ScoreB, match.Pair.B.Name, winner ?? "(draw)");
            if (final && prizes != null)
            {
                var champion = prizes.FirstOrDefault(p => p.Rank == 1);
                await BroadcastAsync(champion != null
                    ? $"{Guilds.Find(champion.SyndicateID)?.Name} is the champion of the Guild PK Tournament! Guild leaders and deputies can claim their prizes from the Awarders in Twin City."
                    : "The Guild PK Tournament is over.");
            }
            else
                await BroadcastAsync($"The Guild PK Tournament {stage} is over. " +
                    string.Join("; ", results.Select(r => r.Winner == null
                        ? $"{r.Match.Pair.A.Name} and {r.Match.Pair.B.Name} drew"
                        : $"{r.Winner} won")) + ".");
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkMatchesEnded).Information(
                "Guild PK Tournament {Stage} ended: {Standings}", stage, StandingsText());
            return true;
        }

        // At the Guild PK Emissary; null when let in, else why not
        public static async Task<string> EnterMatchAsync(Client client, DateTime now)
        {
            var membership = Guilds.Of(client.ID);
            Match match;
            string reason;
            lock (Sync)
            {
                match = membership == null ? null : Matches.FirstOrDefault(m => m.Has(membership.Guild.ID));
                reason =
                    MatchPhase == Phase.Idle ? "closed" :
                    membership == null ? "no_guild" :
                    match == null ? "not_playing" :
                    !LongEnough(membership.Member.Joined, now) ? "new_member" :
                    client.Life.Dead ? "dead" :
                    null;
            }
            if (reason != null)
                return Reject(client, "enter_match", reason);
            var character = client.Character;
            var from = new MapPoint(character.MapID, character.X, character.Y);
            bool first = membership.Guild.ID == match.Pair.A.GuildID;
            await Items.Riding.DismountAsync(client, "guild_pk");
            // Each guild starts on its own side of the flag
            int side = Math.Min(30, match.Map.Grid.Width / 4);
            var (x, y) = Arena.Qualifier.Spot(match.Map.Grid, (first ? -side : side) + Random.Next(-3, 4));
            if (!await Movement.TeleportAsync(client, match.Map, x, y))
                return Reject(client, "enter_match", "teleport");
            lock (Sync)
                if (from.MapID != MatchMapID)
                    Returns[client.ID] = from;
            await client.SendAsync(StatusOf(match, membership.Guild.ID, client.ID, now));
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkEntered).Information(
                "{Name} of {Guild} entered the Guild PK Tournament match {Match}",
                character.Name, membership.Guild.Name, match.Index);
            return null;
        }

        public static bool LongEnough(uint joined, DateTime now) =>
            DateTime.TryParseExact(joined.ToString(CultureInfo.InvariantCulture), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) &&
            (Clock.Local(now).Date - day).TotalDays >= MinGuildDays;

        public static string MatchOf(uint guildID)
        {
            lock (Sync)
            {
                var match = Matches.FirstOrDefault(m => m.Has(guildID));
                if (match == null)
                    return null;
                return match.Pair.A.GuildID == guildID ? match.Pair.B.Name : match.Pair.A.Name;
            }
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(GuildPk), LogEvents.GuildPkFailed).Error(e, "Guild PK loop failed");
                }
            }
        }

        // Each second the flag's holder scores; GM-started events end after RunTime too
        public static async Task TickAsync(DateTime now)
        {
            bool endContest = false, endMatches = false, sendStatus = false, sendRanking = false;
            List<Match> matches;
            lock (Sync)
            {
                if (ContestPhase == Phase.Running && now >= ContestEndsAt)
                    endContest = true;
                if (ContestPhase == Phase.Running && now >= NextRankingAt)
                {
                    NextRankingAt = now + RankingInterval;
                    sendRanking = true;
                }
                if (MatchPhase != Phase.Idle && now >= MatchEndsAt)
                    endMatches = true;
                if (MatchPhase == Phase.Running)
                    foreach (var match in Matches)
                    {
                        if (match.Holder == match.Pair.A.GuildID)
                            match.ScoreA++;
                        else if (match.Holder == match.Pair.B.GuildID)
                            match.ScoreB++;
                    }
                if (MatchPhase != Phase.Idle && now >= NextStatusAt)
                {
                    NextStatusAt = now + StatusInterval;
                    sendStatus = true;
                }
                matches = Matches.ToList();
            }
            if (endContest)
                await EndContestAsync(now);
            if (endMatches)
            {
                await EndMatchesAsync(now);
                return;
            }
            if (sendRanking && Kernel.World.TryGet(ContestMapID, out var contestMap))
            {
                var ranking = RankingPacket();
                foreach (var player in contestMap.All())
                    await player.SendAsync(ranking);
            }
            if (sendStatus)
                foreach (var match in matches)
                    foreach (var player in match.Map.All())
                        if (Guilds.Of(player.ID) is { } m)
                            await player.SendAsync(StatusOf(match, m.Guild.ID, player.ID, now));
        }

        private static MsgFactionMatch StatusOf(Match match, uint guildID, uint characterID, DateTime now)
        {
            lock (Sync)
            {
                bool first = guildID != match.Pair.B.GuildID;
                var (own, other) = first ? (match.Pair.A, match.Pair.B) : (match.Pair.B, match.Pair.A);
                var members = match.Map.All().GroupBy(p => Guilds.Of(p.ID)?.Guild.ID ?? 0)
                    .ToDictionary(g => g.Key, g => (uint)g.Count());
                return new MsgFactionMatch
                {
                    Action = MsgFactionMatch.MatchAction.Status,
                    NameA = own.Name, PointsA = (uint)(first ? match.ScoreA : match.ScoreB),
                    MembersA = members.GetValueOrDefault(own.GuildID),
                    NameB = other.Name, PointsB = (uint)(first ? match.ScoreB : match.ScoreA),
                    MembersB = members.GetValueOrDefault(other.GuildID),
                    SecondsLeft = (uint)Math.Max(0, (MatchEndsAt - now).TotalSeconds),
                    Exploits = Exploits.GetValueOrDefault(characterID),
                };
            }
        }

        // ---- The flag ----

        public static Npc FindFlag(Client client, uint npcID)
        {
            if (client.Map?.Id != MatchMapID)
                return null;
            lock (Sync)
                return Matches.FirstOrDefault(m => m.Map == client.Map && m.Flag.NpcID == npcID)?.Flag;
        }

        // Null when the player may hit the flag, else why not
        public static string FlagProblem(Client client, Npc flag)
        {
            var guild = Guilds.Of(client.ID)?.Guild.ID ?? 0;
            lock (Sync)
            {
                var match = Matches.FirstOrDefault(m => m.Flag == flag);
                return
                    match == null || client.Map != match.Map ? "unknown_target" :
                    MatchPhase != Phase.Running ? "not_started" :
                    !match.Has(guild) ? "not_playing" :
                    match.Holder == guild ? "own_flag" :
                    null;
            }
        }

        public static async Task AfterFlagHitAsync(Client client, Npc flag, uint damage, string kind)
        {
            var membership = Guilds.Of(client.ID);
            if (damage == 0 || membership == null)
                return;
            Match match;
            bool captured;
            uint left;
            lock (Sync)
            {
                match = Matches.FirstOrDefault(m => m.Flag == flag);
                if (match == null || MatchPhase != Phase.Running || !match.Has(membership.Guild.ID) ||
                    match.Holder == membership.Guild.ID)
                    return;
                captured = damage >= match.FlagLeft;
                if (captured)
                {
                    match.Holder = membership.Guild.ID;
                    match.FlagLeft = FlagLife;
                }
                else
                    match.FlagLeft -= damage;
                left = match.FlagLeft;
            }
            if (!captured)
            {
                await SendAroundFlagAsync(match, new MsgUserAttrib(flag.NpcID, AttributeType.Health, left));
                return;
            }
            await SendAroundFlagAsync(match, FlagPacket(match));
            var notice = new MsgTalk(0, MsgTalk.TalkChannel.System,
                $"{client.Character.Name} has seized the flag for {membership.Guild.Name}!");
            foreach (var player in match.Map.All())
                await player.SendAsync(notice);
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkFlagCaptured).Information(
                "{Name} seized the flag of match {Match} for {Guild} ({Kind})",
                client.Character.Name, match.Index, membership.Guild.Name, kind);
        }

        // The flag of the player's match, when it's in view of the cell and wasn't of the previous one
        public static async Task ShowFlagAsync(Client client, ushort x, ushort y, ushort? fromX = null, ushort? fromY = null)
        {
            if (client.Map?.Id != MatchMapID)
                return;
            Match match;
            lock (Sync)
                match = Matches.FirstOrDefault(m => m.Map == client.Map);
            if (match == null || !GameMap<Client>.InView(x, y, match.Flag.X, match.Flag.Y) ||
                fromX.HasValue && GameMap<Client>.InView(fromX.Value, fromY.Value, match.Flag.X, match.Flag.Y))
                return;
            await client.SendAsync(FlagPacket(match));
        }

        private static MsgNpcInfoEx FlagPacket(Match match)
        {
            lock (Sync)
                return new MsgNpcInfoEx(match.Flag, FlagLife, match.FlagLeft, match.Flag.Lookface,
                    match.Holder == 0 ? FlagName : Guilds.Find(match.Holder)?.Name ?? FlagName);
        }

        private static async Task SendAroundFlagAsync(Match match, Network.Packets.IPacket packet)
        {
            foreach (var player in match.Map.InView(match.Flag.X, match.Flag.Y))
                await player.SendAsync(packet);
        }

        // ---- Fighting in a match ----

        // Combat asks before a hit on a player: null when the attacker isn't in a match, else
        // whether the two may fight: guilds of the match against each other, once it started
        public static bool? CanFight(Client attacker, Client target)
        {
            if (attacker.Map?.Id != MatchMapID)
                return null;
            uint a = Guilds.Of(attacker.ID)?.Guild.ID ?? 0, b = Guilds.Of(target.ID)?.Guild.ID ?? 0;
            lock (Sync)
            {
                var match = Matches.FirstOrDefault(m => m.Map == attacker.Map);
                if (match == null)
                    return null;
                return MatchPhase == Phase.Running && target.Map == attacker.Map &&
                    match.Has(a) && match.Has(b) && a != b;
            }
        }

        // True when the kill was in a match: an Exploit for the killer, and no PK
        public static async Task<bool> OnKillAsync(Client killer, Client victim)
        {
            if (killer.Map?.Id != MatchMapID)
                return false;
            uint exploits;
            lock (Sync)
            {
                if (!Matches.Any(m => m.Map == killer.Map))
                    return false;
                exploits = AddExploit(killer.ID);
            }
            await killer.SendAsync(StatusFor(killer, exploits));
            return true;
        }

        // Reviving a fellow guild member in a match is an Exploit
        public static async Task OnReviveAsync(Client reviver, Client target)
        {
            if (reviver.Map?.Id != MatchMapID || target.Map != reviver.Map ||
                Guilds.Of(reviver.ID)?.Guild.ID is not uint guild || Guilds.Of(target.ID)?.Guild.ID != guild)
                return;
            uint exploits;
            lock (Sync)
            {
                if (MatchPhase != Phase.Running || !Matches.Any(m => m.Map == reviver.Map))
                    return;
                exploits = AddExploit(reviver.ID);
            }
            await reviver.SendAsync(StatusFor(reviver, exploits));
        }

        private static MsgFactionMatch StatusFor(Client client, uint exploits)
        {
            Match match;
            lock (Sync)
                match = Matches.FirstOrDefault(m => m.Map == client.Map);
            var guild = Guilds.Of(client.ID)?.Guild.ID ?? 0;
            var status = match == null ? new MsgFactionMatch() : StatusOf(match, guild, client.ID, DateTime.UtcNow);
            status.Exploits = exploits;
            return status;
        }

        public static bool IsTakingPart(Client client) =>
            client.Map?.Id == MatchMapID && client.Character != null;

        // ---- Prizes ----

        public static DbGuildPkPrize PrizeOf(uint guildID, byte kind, DateTime now)
        {
            lock (Sync)
                return Prizes.TryGetValue((guildID, kind), out var prize) && now < prize.Expires ? prize : null;
        }

        public static uint ConquerPointsOf(byte kind, int rank)
        {
            var table = kind == ContestPrize ? ContestPrizes : TournamentPrizes;
            return rank >= 1 && rank <= table.Length ? table[rank - 1] : 0;
        }

        // At the Awarder of a place; null when claimed, else why not
        public static async Task<string> ClaimAsync(Client client, byte kind, int place, DateTime now)
        {
            var membership = Guilds.Of(client.ID);
            DbGuildPkPrize prize;
            string reason;
            lock (Sync)
            {
                prize = membership == null ? null : PrizeOf(membership.Guild.ID, kind, now);
                reason =
                    membership == null ? "no_guild" :
                    membership.Member.Rank < GuildRank.DeputyLeader ? "not_leader" :
                    prize == null ? "no_prize" :
                    prize.Claimed ? "claimed" :
                    prize.Rank != place ? "other_awarder" :
                    null;
                if (reason == null)
                    prize.Claimed = true;
            }
            if (reason != null)
                return Reject(client, "claim", reason);
            uint cps = ConquerPointsOf(kind, prize.Rank);
            if (cps > 0 && !await Progress.ChangeConquerPointsAsync(client, cps, kind == ContestPrize ? "guild_contest_prize" : "guild_pk_prize"))
            {
                lock (Sync)
                    prize.Claimed = false;
                return Reject(client, "claim", "pay_failed");
            }
            await GuildPkRepository.SetClaimedAsync(prize.SyndicateID, kind);
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkPrizeClaimed).Information(
                "{Name} claimed the {Kind} prize of {Guild}: place {Rank}, {ConquerPoints} CPs",
                client.Character.Name, kind == ContestPrize ? "Guild Contest" : "Guild PK Tournament",
                membership.Guild.Name, prize.Rank, cps);
            return null;
        }

        // ---- Leaving ----

        // Logout: the player's return point is forgotten (the maps aren't recorded)
        public static void Leave(Client client)
        {
            lock (Sync)
                Returns.Remove(client.ID);
        }

        public static string StatusText(DateTime now)
        {
            lock (Sync)
            {
                string contest = $"Guild Contest {ContestPhase}" +
                    (ContestPoints.Count > 0 ? $" ({ContestPoints.Count} guilds scoring)" : "");
                string matches = MatchPhase == Phase.Idle ? "matches Idle"
                    : $"{(FinalStage ? "final" : $"round {RoundNumber}")} {MatchPhase}: " +
                      string.Join(", ", Matches.Select(m => $"{m.Pair.A.Name} {m.ScoreA}-{m.ScoreB} {m.Pair.B.Name}"));
                return $"{contest}; {matches}. Standings: {StandingsText()}. " +
                    $"{Prizes.Values.Count(p => !p.Claimed && now < p.Expires)} prizes unclaimed.";
            }
        }

        private static string StandingsText()
        {
            lock (Sync)
            {
                if (Entries.Count == 0)
                    return "none";
                return string.Join(" | ", new[] { 0, 1 }.Select(h => GuildPkTable.HalfName(h) + ": " +
                    string.Join(", ", GuildPkTable.Standings(Entries.Values, h)
                        .Select(e => $"{e.Name} {e.Points}pts" + (e.FinalRank > 0 ? $" #{e.FinalRank}" : "")))));
            }
        }

        // ---- Helpers ----

        private static uint AddExploit(uint characterID)
        {
            uint exploits = Exploits[characterID] = Exploits.GetValueOrDefault(characterID) + 1;
            ExploitsChanged.Add(characterID);
            return exploits;
        }

        private static async Task SaveExploitsAsync()
        {
            List<DbGuildPkExploit> rows;
            lock (Sync)
            {
                rows = ExploitsChanged.Select(id => new DbGuildPkExploit { CharacterID = id, Exploits = Exploits[id] }).ToList();
                ExploitsChanged.Clear();
            }
            if (rows.Count > 0)
                await GuildPkRepository.SaveExploitsAsync(rows);
        }

        private static DbGuildPkEntry ToRow(GuildPkEntry e) => new DbGuildPkEntry
        {
            SyndicateID = e.GuildID, Name = e.Name ?? string.Empty, Seed = (byte)e.Seed, ContestPoints = e.ContestPoints,
            Played = (byte)e.Played, Points = e.Points, Score = e.Score, FinalRank = (byte)e.FinalRank,
        };

        // Tournament prizes are claimed until the next final opens
        public static DateTime NextSaturdayEvening(DateTime local)
        {
            int days = ((int)DayOfWeek.Saturday - (int)local.DayOfWeek + 7) % 7;
            var at = local.Date.AddDays(days == 0 ? 7 : days) + new TimeSpan(20, 30, 0);
            return at;
        }

        private static DateTime ToUtc(DateTime local) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Clock.Zone);

        private static async Task SendHomeAsync(Client client)
        {
            if (client.Character == null)
                return;
            if (client.Character.MapID == MatchMapID)
                await client.SendAsync(new MsgFactionMatch { Action = MsgFactionMatch.MatchAction.Close });
            if (client.Character.MapID != MatchMapID && client.Character.MapID != ContestMapID)
                return;
            if (client.Life.Dead)
                await Combat.Death.ReviveHereAsync(client, "guild_pk");
            MapPoint back;
            lock (Sync)
            {
                back = Returns.GetValueOrDefault(client.ID);
                Returns.Remove(client.ID);
            }
            if (back == null || !await Movement.TeleportAsync(client, back.MapID, back.X, back.Y))
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
        }

        private static async Task BroadcastAsync(string text)
        {
            var message = new MsgTalk(0, MsgTalk.TalkChannel.Center, text);
            foreach (var player in Kernel.Players.Values.ToList())
                if (player.Character != null)
                    await player.SendAsync(message);
        }

        private static string Reject(Client client, string kind, string reason)
        {
            LogEvents.For(typeof(GuildPk), LogEvents.GuildPkRejected).Information(
                "{Name} can't {Kind} in the Guild PK Tournament: {Reason}", client.Character?.Name, kind, reason);
            return reason;
        }
    }
}

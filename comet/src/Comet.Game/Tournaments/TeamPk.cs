namespace Comet.Game.Tournaments
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
    using Comet.Game.Events;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgTeamPKArenic;

    // The weekly Team PK Tournament (Saturday) and Skill Team PK Tournament (Wednesday), one
    // per level group, timed by the client's Activity.ini "Team PK" and "Skill Team PK"
    // entries: sign-up opens at 19:45 and the groups start from 20:00, the 130+ group first,
    // two minutes apart. A team's leader signs it up at the event's manager in Twin City, then
    // its members do; members above the leader's level group can't. They wait in the group's
    // waiting room. At the start the members still in the leader's team fight for it and the
    // rest are sent out; a team whose leader left or that was dismissed is out, then and between
    // rounds. Knock-out rounds of team-on-team matches follow, each in its own arena: 10
    // minutes, a team with nobody left standing loses, else the higher damage score, then more
    // standing. The fallen stay down until the match is over. From the last eight on, matches
    // last 15 minutes and the semifinal losers fight for third. Every match gives each fighter
    // the event's EXP pack and 1,000 Honor Points. The final eight's leaders claim a pack at
    // the manager until the group's next tournament opens. The Skill Team PK caps gear and
    // more (SkillCaps) on its maps.
    public sealed class TeamPk
    {
        // Champion, 2nd, 3rd, Top 8 packs four apart from PackBase, by group: Refined, Unique,
        // Elite, Super Team PK packs; Perfect, Outstanding, Superior, Delicate Skill Team PK packs
        public static readonly TeamPk Team = new TeamPk(0, "Team PK", "Team PK Tournament", 8158,
            PacketType.MsgTeamPKArenic, PacketType.MsgTeamPKArenicScore, expPack: 720793, packBase: 720794, capped: false);
        public static readonly TeamPk Skill = new TeamPk(1, "Skill Team PK", "Skill Team PK Tournament", 8592,
            PacketType.MsgTeamPopPKArenic, PacketType.MsgTeamPopPKArenicScore, expPack: 720981, packBase: 721300, capped: true);
        public static readonly IReadOnlyList<TeamPk> All = new[] { Team, Skill };

        public const uint WaitingMapID = ElitePk.WaitingMapID;
        public const uint ArenaMapID = ElitePk.ArenaMapID;
        public const int Groups = ElitePk.Groups;
        public const uint HonorPerMatch = 1000;

        public static TimeSpan KnockoutTime { get; set; } = TimeSpan.FromMinutes(10);
        public static TimeSpan FinalTime { get; set; } = TimeSpan.FromMinutes(15);
        public static TimeSpan CountdownTime { get; set; } = TimeSpan.FromSeconds(10);
        public static TimeSpan RoundBreak { get; set; } = TimeSpan.FromSeconds(15);
        public static TimeSpan LeaveDelay { get; set; } = TimeSpan.FromSeconds(3);

        public static IReadOnlyList<string> GroupNames => ElitePk.GroupNames;

        public static int GroupOf(int level) => ElitePk.GroupOf(level);

        public enum Phase { Idle, SignUp, Running }

        public byte ID { get; }
        public string EventName { get; }
        public string Title { get; }
        public uint ManagerID { get; }
        public uint ExpPackType { get; }
        public uint PackBase { get; }
        public bool Capped { get; }
        private readonly PacketType arenicType, scoreType;

        private sealed class Entry
        {
            public uint Leader;
            public string LeaderName;
            // The leader first
            public readonly List<uint> Members = new List<uint>();
            public string Name => $"Team of {this.LeaderName}";
        }

        private sealed class Match
        {
            public Entry A, B;
            public bool ThirdPlace;
            public GameMap<Client> Map;
            public DateTime FightAt, EndsAt;
            public uint ScoreA, ScoreB;
            public bool Fighting, Over, Moved;
            public uint Winner;
            public DateTime OverAt;
            public readonly HashSet<uint> Fought = new HashSet<uint>();

            public bool Has(uint leader) => leader == this.A.Leader || leader == this.B.Leader;
            public Entry Other(uint leader) => leader == this.A.Leader ? this.B : this.A;
            public IEnumerable<uint> Fighters => this.A.Members.Concat(this.B.Members);
        }

        private sealed class Tournament
        {
            public int Group;
            public Phase Phase;
            public GameMap<Client> WaitingRoom;
            // By leader
            public readonly Dictionary<uint, Entry> Teams = new Dictionary<uint, Entry>();
            public readonly Dictionary<uint, MapPoint> Returns = new Dictionary<uint, MapPoint>();
            public EliteBracket Bracket;
            public readonly List<Match> Matches = new List<Match>();
            public DateTime NextRoundAt;
            public TimeSpan MatchTime;
        }

        // One lock for both events: a player takes part in one at a time
        private static readonly object Sync = new object();
        private static readonly Random Random = new Random();

        private readonly Tournament[] tournaments = Enumerable.Range(0, Groups)
            .Select(g => new Tournament { Group = g }).ToArray();
        private readonly Dictionary<uint, DbTeamPkReward> rewards = new Dictionary<uint, DbTeamPkReward>();

        private TeamPk(byte id, string eventName, string title, uint managerID, PacketType arenic, PacketType score,
            uint expPack, uint packBase, bool capped)
        {
            this.ID = id;
            this.EventName = eventName;
            this.Title = title;
            this.ManagerID = managerID;
            this.arenicType = arenic;
            this.scoreType = score;
            this.ExpPackType = expPack;
            this.PackBase = packBase;
            this.Capped = capped;
        }

        public uint PackOf(int group, int rank) => this.PackBase + (uint)group + 4u * (uint)(rank - 1);

        public static TeamPk ByManager(uint npcID) => All.FirstOrDefault(e => e.ManagerID == npcID);

        public static void SubscribeAll()
        {
            foreach (var ev in All)
                EventScheduler.Subscribe(ev.EventName, ev.OnEdgeAsync);
        }

        public static async Task LoadAllAsync()
        {
            foreach (var ev in All)
            {
                var rows = await TeamPkRepository.LoadAsync(ev.ID);
                lock (Sync)
                {
                    ev.rewards.Clear();
                    foreach (var row in rows)
                        ev.rewards[row.LeaderID] = row;
                }
                LogEvents.For(typeof(TeamPk), LogEvents.TeamPkLoaded).Information(
                    "Loaded {RewardCount} {EventName} placements", rows.Count, ev.EventName);
            }
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try
                {
                    foreach (var ev in All)
                        await ev.TickAsync(DateTime.UtcNow);
                }
                catch (Exception e)
                {
                    LogEvents.For(typeof(TeamPk), LogEvents.TeamPkFailed).Error(e, "Team PK loop failed");
                }
            }
        }

        public Phase PhaseOf(int group)
        {
            lock (Sync)
                return this.tournaments[group].Phase;
        }

        public DbTeamPkReward RewardOf(uint leaderID)
        {
            lock (Sync)
                return this.rewards.GetValueOrDefault(leaderID);
        }

        // The Activity.ini entry of a group, by its level range
        public ScheduledEvent EventOf(int group) =>
            EventScheduler.Schedule.Named(this.EventName).FirstOrDefault(e => GroupOf(e.LevelMin) == group);

        private Task OnEdgeAsync(ScheduledEvent ev, EventWindow window, EventEdge edge, DateTime now)
        {
            int group = GroupOf(ev.LevelMin);
            return edge switch
            {
                EventEdge.SignUpOpens => this.OpenAsync(group, now),
                EventEdge.Starts => this.StartAsync(group, now),
                _ => Task.CompletedTask,
            };
        }

        // Sign-up opens; last week's unclaimed prizes of the group are gone
        public async Task<bool> OpenAsync(int group, DateTime now)
        {
            lock (Sync)
            {
                var t = this.tournaments[group];
                if (t.Phase != Phase.Idle)
                    return false;
                if (Kernel.Maps?.Exists(WaitingMapID) != true || Kernel.Maps?.Exists(ArenaMapID) != true)
                {
                    LogEvents.For(typeof(TeamPk), LogEvents.TeamPkFailed).Error(
                        "No {EventName} maps: {WaitingMapId} or {ArenaMapId} isn't loaded", this.EventName, WaitingMapID, ArenaMapID);
                    return false;
                }
                t.Phase = Phase.SignUp;
                t.WaitingRoom = new GameMap<Client>(WaitingMapID, Kernel.Maps.Get(WaitingMapID));
                if (this.Capped)
                    SkillCaps.Add(t.WaitingRoom);
                t.Teams.Clear();
                t.Returns.Clear();
                t.Matches.Clear();
                t.Bracket = null;
                foreach (var id in this.rewards.Where(r => r.Value.Group == group).Select(r => r.Key).ToList())
                    this.rewards.Remove(id);
            }
            await TeamPkRepository.ClearGroupAsync(this.ID, (byte)group);
            await Talk.BroadcastAsync($"The {this.Title} ({GroupNames[group]}) is open! Team leaders, sign your team up with the {this.EventName} Manager in Twin City before it starts.");
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkOpened).Information(
                "{EventName} sign-up opened for group {Group} ({GroupName})", this.EventName, group, GroupNames[group]);
            return true;
        }

        // At the manager: the leader signs the team up, then each member; null when signed up,
        // else why not
        public async Task<string> SignUpAsync(Client client)
        {
            var character = client.Character;
            var leader = client.Team?.Leader;
            string otherEvent = EventHelpers.CheckOtherEvents(client,
                (GameEvent.Qualifier | GameEvent.ElitePk, "qualifier"));
            Tournament t = null;
            string reason;
            lock (Sync)
            {
                if (leader == client)
                    t = this.tournaments[GroupOf(character.Level)];
                else if (leader != null)
                    t = this.tournaments.FirstOrDefault(x => x.Phase != Phase.Idle && x.Teams.ContainsKey(leader.ID));
                reason =
                    leader == null ? "no_team" :
                    t == null ? "leader_first" :
                    t.Phase == Phase.Running ? "started" :
                    t.Phase != Phase.SignUp ? "closed" :
                    IsTakingPartLocked(client.ID) ? "signed_up" :
                    leader != client && GroupOf(character.Level) > t.Group ? "level" :
                    client.Life.Dead ? "dead" :
                    otherEvent;
                if (reason == null)
                {
                    if (leader == client)
                        t.Teams[client.ID] = new Entry { Leader = client.ID, LeaderName = character.Name };
                    t.Teams[leader.ID].Members.Add(client.ID);
                    t.Returns[client.ID] = new MapPoint(character.MapID, character.X, character.Y);
                }
            }
            if (reason != null)
            {
                this.Reject(client, "sign_up", reason);
                return reason;
            }
            await Riding.DismountAsync(client, "team_pk");
            var (x, y) = WaitingSpot(t.WaitingRoom.Grid);
            if (!await Movement.TeleportAsync(client, t.WaitingRoom, x, y))
            {
                lock (Sync)
                    this.Remove(t, client.ID);
                this.Reject(client, "sign_up", "teleport");
                return "teleport";
            }
            await Talk.NoticeAsync(client, $"You are in the waiting area for the {this.Title}.");
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkSignedUp).Information(
                "{Name} signed up for the {EventName} Tournament in the team of {Leader}, group {Group} (level {Level})",
                character.Name, this.EventName, leader.Character.Name, t.Group, character.Level);
            return null;
        }

        public async Task<bool> StartAsync(int group, DateTime now)
        {
            var t = this.tournaments[group];
            List<uint> sendHome;
            int count;
            bool cancelled;
            lock (Sync)
            {
                if (t.Phase != Phase.SignUp)
                    return false;
                // Only the teams whose leader still leads them in the waiting room, with the
                // members still in them there
                foreach (var entry in t.Teams.Values.ToList())
                {
                    if (!this.Prune(t, entry))
                        t.Teams.Remove(entry.Leader);
                }
                var fighting = t.Teams.Values.SelectMany(e => e.Members).ToHashSet();
                sendHome = t.Returns.Keys.Where(id => !fighting.Contains(id)).ToList();
                count = t.Teams.Count;
                cancelled = count < 2;
                if (cancelled)
                {
                    sendHome = t.Returns.Keys.ToList();
                    this.Close(t);
                }
                else
                {
                    t.Phase = Phase.Running;
                    t.Bracket = new EliteBracket(t.Teams.Keys);
                    t.NextRoundAt = now;
                }
            }
            foreach (var id in sendHome)
                if (Kernel.Players.TryGet(id, out var player))
                    Kernel.Post(player, async () =>
                    {
                        if (player.Map != t.WaitingRoom)
                            return;
                        await Talk.NoticeAsync(player, cancelled
                            ? $"Not enough teams signed up. The {this.Title} is cancelled."
                            : $"You are not in a team that signed up for the {this.Title}.");
                        await this.SendHomeAsync(player, t);
                    });
            if (cancelled)
            {
                LogEvents.For(typeof(TeamPk), LogEvents.TeamPkEnded).Information(
                    "{EventName} group {Group} cancelled: {Count} teams", this.EventName, group, count);
                return false;
            }
            await Talk.BroadcastAsync($"The {this.Title} ({GroupNames[group]}) has started with {count} teams!");
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkStarted).Information(
                "{EventName} group {Group} started with {Count} teams", this.EventName, group, count);
            return true;
        }

        public async Task TickAsync(DateTime now)
        {
            for (int group = 0; group < Groups; group++)
                await this.TickAsync(this.tournaments[group], now);
        }

        private async Task TickAsync(Tournament t, DateTime now)
        {
            var decided = new List<(Match Match, uint Winner, string Reason)>();
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
                    if (!match.Over && match.Fighting)
                    {
                        int a = Standing(match, match.A), b = Standing(match, match.B);
                        if (a == 0 || b == 0)
                            decided.Add((match, a > 0 ? match.A.Leader : b > 0 ? match.B.Leader : WinnerOnScore(match, a, b), "wiped"));
                        else if (now >= match.EndsAt)
                            decided.Add((match, WinnerOnScore(match, a, b), "time"));
                    }
                    if (match.Over && !match.Moved && now >= match.OverAt + LeaveDelay)
                    {
                        match.Moved = true;
                        toMove.Add(match);
                    }
                }
            }
            foreach (var (match, winner, reason) in decided)
                await this.DecideAsync(t, match, winner, reason, now);
            foreach (var match in toMove)
                this.MoveOut(t, match);

            lock (Sync)
            {
                if (t.Matches.Any(m => !m.Moved))
                    return;
                if (t.Bracket.Finished)
                    finished = true;
                else if (now >= t.NextRoundAt)
                {
                    foreach (var match in t.Matches)
                        SkillCaps.Remove(match.Map);
                    t.Matches.Clear();
                    foreach (var leader in t.Bracket.Alive.ToList())
                        if (!t.Teams.TryGetValue(leader, out var entry) || !this.Prune(t, entry))
                            t.Bracket.Withdraw(leader);
                    if (t.Bracket.Finished)
                        finished = true;
                    else
                    {
                        round = t.Bracket.NextRound(Random);
                        if (round == null)
                            return;
                        t.MatchTime = round.Stage == EliteStage.Knockout ? KnockoutTime : FinalTime;
                        foreach (var (a, b) in round.Pairs)
                            t.Matches.Add(this.NewMatch(t.Teams[a], t.Teams[b], false, now, t.MatchTime));
                        if (round.ThirdPlace is (uint x, uint y))
                            t.Matches.Add(this.NewMatch(t.Teams[x], t.Teams[y], true, now, t.MatchTime));
                    }
                }
            }
            if (finished)
                await this.FinishAsync(t);
            else if (round != null)
                await this.BeginRoundAsync(t, round);
        }

        // Drops the members gone from the waiting room or from the team; false when the team is
        // out: its leader is gone or no longer leads it. Under the lock.
        private bool Prune(Tournament t, Entry entry)
        {
            if (!Kernel.Players.TryGet(entry.Leader, out var leader) || leader.Map != t.WaitingRoom ||
                leader.Team?.Leader != leader)
                return false;
            entry.Members.RemoveAll(id => !Kernel.Players.TryGet(id, out var p) || p.Map != t.WaitingRoom || p.Team != leader.Team);
            return true;
        }

        private Match NewMatch(Entry a, Entry b, bool third, DateTime now, TimeSpan length)
        {
            var match = new Match
            {
                A = a, B = b, ThirdPlace = third,
                Map = new GameMap<Client>(ArenaMapID, Kernel.Maps.Get(ArenaMapID)),
                FightAt = now + CountdownTime,
                EndsAt = now + CountdownTime + length,
            };
            if (this.Capped)
                SkillCaps.Add(match.Map);
            return match;
        }

        private async Task BeginRoundAsync(Tournament t, EliteRound round)
        {
            List<Match> matches;
            lock (Sync)
                matches = t.Matches.ToList();
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkRound).Information(
                "{EventName} group {Group} round {Round} ({Stage}): {Pairs}, bye {Bye}",
                this.EventName, t.Group, t.Bracket.RoundNumber, round.Stage,
                string.Join(", ", matches.Select(m => $"{m.A.LeaderName}-{m.B.LeaderName}")), round.Bye);

            string stage = round.Stage switch
            {
                EliteStage.Knockout => $"Knock-out round {t.Bracket.RoundNumber}",
                EliteStage.Quarterfinal => "The quarterfinal",
                EliteStage.Semifinal => "The semifinal",
                _ => "The final",
            };
            await this.GroupNoticeAsync(t, $"{stage} of the {this.Title} begins!");
            if (round.Bye != 0)
                foreach (var id in MembersOf(t, round.Bye))
                    if (Kernel.Players.TryGet(id, out var lucky))
                        await Talk.NoticeAsync(lucky, "Your team has no opponent this round and goes through to the next one.");

            foreach (var match in matches)
                foreach (var side in new[] { match.A, match.B })
                {
                    List<uint> members;
                    lock (Sync)
                        members = side.Members.ToList();
                    for (int i = 0; i < members.Count; i++)
                    {
                        int spot = (side == match.A ? -1 : 1) * (1 + i);
                        if (Kernel.Players.TryGet(members[i], out var fighter))
                            Kernel.Post(fighter, () => this.EnterAsync(fighter, t, match, side, spot));
                    }
                }
        }

        // On the fighter's queue
        private async Task EnterAsync(Client client, Tournament t, Match match, Entry side, int spot)
        {
            if (client.Character == null || client.Map != t.WaitingRoom)
                return;
            lock (Sync)
                if (match.Over)
                    return;
            if (client.Life.Dead)
                await Combat.Death.ReviveHereAsync(client, "team_pk");
            var (x, y) = Arena.Qualifier.Spot(match.Map.Grid, spot);
            if (!await Movement.TeleportAsync(client, match.Map, x, y))
                return;
            lock (Sync)
                match.Fought.Add(client.ID);
            await ElitePk.RestoreAsync(client);
            await client.SendAsync(new MsgTeamPKArenic(this.arenicType, ArenicAction.Start) { Seconds = (ushort)t.MatchTime.TotalSeconds });
            await client.SendAsync(this.ScoreOf(match));
            await Talk.NoticeAsync(client, $"Your opponent is the {match.Other(side.Leader).Name}. The match starts in {CountdownTime.TotalSeconds:0} seconds.");
        }

        // A team wiped out or the clock; the winner gets the bracket's next round
        private async Task DecideAsync(Tournament t, Match match, uint winner, string reason, DateTime now)
        {
            MsgTeamPKArenicScore score;
            List<uint> fought;
            lock (Sync)
            {
                if (match.Over)
                    return;
                match.Over = true;
                match.Winner = winner;
                match.OverAt = now;
                t.Bracket.Record(winner, match.Other(winner).Leader);
                if (!t.Bracket.RoundOpen)
                    t.NextRoundAt = now + LeaveDelay + RoundBreak;
                score = this.ScoreOf(match);
                fought = match.Fought.ToList();
            }
            var winners = winner == match.A.Leader ? match.A : match.B;
            var losers = match.Other(winner);
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkMatchEnded).Information(
                "{EventName} group {Group}: {Winner} beat {Loser} ({Reason}, {ScoreA}-{ScoreB}{Third})",
                this.EventName, t.Group, winners.Name, losers.Name, reason, match.ScoreA, match.ScoreB,
                match.ThirdPlace ? ", third place" : "");
            foreach (var id in fought)
                if (Kernel.Players.TryGet(id, out var fighter) && fighter.Character != null)
                {
                    await fighter.SendAsync(score);
                    await fighter.SendAsync(new MsgTeamPKArenic(this.arenicType, ArenicAction.Result)
                    {
                        Won = winners.Members.Contains(id),
                    });
                    Kernel.Post(fighter, () => this.RewardMatchAsync(fighter));
                }
            await this.GroupNoticeAsync(t, $"The {winners.Name} has defeated the {losers.Name} in the {this.Title}.");
        }

        // More damage dealt, then more standing; ties go to the first
        private static uint WinnerOnScore(Match match, int standingA, int standingB) =>
            Compare(match.ScoreA, standingA, match.ScoreB, standingB) >= 0 ? match.A.Leader : match.B.Leader;

        public static int Compare(uint scoreA, int standingA, uint scoreB, int standingB) =>
            scoreA != scoreB ? scoreA.CompareTo(scoreB) : standingA.CompareTo(standingB);

        // Fighters of a side alive in its arena and still in the team. Under the lock.
        private static int Standing(Match match, Entry side) =>
            side.Members.Count(id => Kernel.Players.TryGet(id, out var p) && p.Character != null &&
                p.Map == match.Map && !p.Life.Dead && p.Team?.Leader?.ID == side.Leader);

        // On the fighter's queue
        private async Task RewardMatchAsync(Client client)
        {
            if (client.Character == null)
                return;
            Arena.Qualifier.AddHonor(client, HonorPerMatch);
            if (Kernel.ItemTypes.TryGet(this.ExpPackType, out var pack) && await ItemActions.GiveAsync(client, pack, "team_pk_match") == null)
                await Talk.NoticeAsync(client, "Your inventory is full; the EXP pack is lost.");
            await client.SendAsync(new MsgTeamPKArenic(this.arenicType, ArenicAction.Awarded));
        }

        private void MoveOut(Tournament t, Match match)
        {
            List<uint> fighters;
            lock (Sync)
                fighters = match.Fought.ToList();
            foreach (var id in fighters)
            {
                if (!Kernel.Players.TryGet(id, out var fighter))
                    continue;
                bool stays;
                lock (Sync)
                {
                    uint leader = match.A.Members.Contains(id) ? match.A.Leader : match.B.Leader;
                    stays = !t.Bracket.Finished &&
                        (leader == match.Winner && !match.ThirdPlace || t.Bracket.AwaitsThirdPlace(leader));
                }
                Kernel.Post(fighter, async () =>
                {
                    if (fighter.Character == null || fighter.Map != match.Map)
                        return;
                    if (fighter.Life.Dead)
                        await Combat.Death.ReviveHereAsync(fighter, "team_pk");
                    await ElitePk.RestoreAsync(fighter);
                    await fighter.SendAsync(new MsgTeamPKArenic(this.arenicType, ArenicAction.Leave));
                    if (stays)
                    {
                        var (x, y) = WaitingSpot(t.WaitingRoom.Grid);
                        if (await Movement.TeleportAsync(fighter, t.WaitingRoom, x, y))
                            return;
                    }
                    await this.SendHomeAsync(fighter, t);
                });
            }
        }

        private async Task FinishAsync(Tournament t)
        {
            IReadOnlyList<(uint ID, int Rank)> placements;
            List<uint> waiting;
            List<DbTeamPkReward> rows;
            lock (Sync)
            {
                if (t.Phase != Phase.Running)
                    return;
                placements = t.Bracket.Placements();
                waiting = t.Returns.Keys.ToList();
                rows = placements.Select(p => new DbTeamPkReward
                {
                    Event = this.ID, LeaderID = p.ID, Group = (byte)t.Group, Rank = (byte)p.Rank,
                    Name = t.Teams.TryGetValue(p.ID, out var e) ? e.LeaderName : p.ID.ToString(),
                }).ToList();
                foreach (var row in rows)
                    this.rewards[row.LeaderID] = new DbTeamPkReward
                    {
                        Event = row.Event, LeaderID = row.LeaderID, Group = row.Group, Rank = row.Rank, Name = row.Name,
                    };
                this.Close(t);
            }
            await TeamPkRepository.ReplaceGroupAsync(this.ID, (byte)t.Group, rows);

            foreach (var id in waiting)
                if (Kernel.Players.TryGet(id, out var player))
                    Kernel.Post(player, async () =>
                    {
                        if (player.Map == t.WaitingRoom)
                            await this.SendHomeAsync(player, t);
                    });

            var champion = rows.FirstOrDefault(p => p.Rank == EliteBracket.Champion);
            await Talk.BroadcastAsync(champion != null
                ? $"The Team of {champion.Name} is the champion of the {this.Title} ({GroupNames[t.Group]})! The leaders of the final eight can claim their prizes from the {this.EventName} Manager."
                : $"The {this.Title} ({GroupNames[t.Group]}) is over.");
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkEnded).Information(
                "{EventName} group {Group} ended: {Placements}", this.EventName, t.Group,
                string.Join(", ", rows.Select(p => $"{p.Rank}:{p.Name}")));
        }

        // Back to idle; the maps stop being capped. Under the lock.
        private void Close(Tournament t)
        {
            t.Phase = Phase.Idle;
            SkillCaps.Remove(t.WaitingRoom);
            foreach (var match in t.Matches)
                SkillCaps.Remove(match.Map);
            t.Matches.Clear();
        }

        // At the manager, by the leader; null when claimed, else why not
        public async Task<string> ClaimAsync(Client client)
        {
            DbTeamPkReward reward;
            string reason;
            lock (Sync)
            {
                reward = this.rewards.GetValueOrDefault(client.ID);
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
                this.Reject(client, "claim", reason);
                return reason;
            }
            uint packType = this.PackOf(reward.Group, reward.Rank);
            if (!Kernel.ItemTypes.TryGet(packType, out var type) ||
                await ItemActions.GiveAsync(client, type, "team_pk_prize") == null)
            {
                lock (Sync)
                    reward.Claimed = false;
                this.Reject(client, "claim", "no_item");
                return "no_item";
            }
            await TeamPkRepository.SetClaimedAsync(this.ID, client.ID, true);
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkRewardClaimed).Information(
                "{Name} claimed the {EventName} prize: group {Group}, rank {Rank}, pack {TypeId}",
                client.Character.Name, this.EventName, reward.Group, reward.Rank, packType);
            return null;
        }

        // Combat asks before a hit on a player: null when the attacker isn't in a team match's
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
                return match.Fighting && !match.Over && target.Map == attacker.Map &&
                    SideOf(match, attacker.ID) is Entry a && SideOf(match, target.ID) is Entry b && a != b;
            }
        }

        // On the target's queue, after the damage was taken
        public static async Task OnHitAsync(Client attacker, Client target, uint damage)
        {
            if (damage == 0 || attacker.Map?.Id != ArenaMapID)
                return;
            MsgTeamPKArenicScore score;
            List<uint> fighters;
            lock (Sync)
            {
                var match = MatchOn(attacker.Map);
                if (match == null || !match.Fighting || match.Over)
                    return;
                var side = SideOf(match, attacker.ID);
                if (side == null || SideOf(match, target.ID) is not Entry other || other == side)
                    return;
                if (side == match.A)
                    match.ScoreA += damage;
                else
                    match.ScoreB += damage;
                score = EventOf(match).ScoreOf(match);
                fighters = match.Fought.ToList();
            }
            foreach (var id in fighters)
                if (Kernel.Players.TryGet(id, out var fighter) && fighter.Map == attacker.Map)
                    await fighter.SendAsync(score);
        }

        // True if the kill was in a team match: then it's no PK, and the match decides on its
        // next tick if the team was wiped out
        public static Task<bool> OnKillAsync(Client killer, Client victim)
        {
            if (killer.Map?.Id != ArenaMapID)
                return Task.FromResult(false);
            lock (Sync)
            {
                var match = MatchOn(killer.Map);
                return Task.FromResult(match != null && SideOf(match, victim.ID) != null);
            }
        }

        // The fallen of a match stay down until it's over
        public static bool BlocksRevive(Client client)
        {
            if (client.Map?.Id != ArenaMapID)
                return false;
            lock (Sync)
                return MatchOn(client.Map) is Match match && !match.Over;
        }

        // Logout: out of the team's roster; a team losing its leader drops out between rounds
        public static Task LeaveAsync(Client client, string reason)
        {
            foreach (var ev in All)
            {
                Tournament t = null;
                lock (Sync)
                {
                    t = ev.tournaments.FirstOrDefault(x => x.Phase != Phase.Idle && x.Returns.ContainsKey(client.ID));
                    if (t != null && t.Phase == Phase.SignUp)
                        ev.Remove(t, client.ID);
                }
                if (t != null)
                    LogEvents.For(typeof(TeamPk), LogEvents.TeamPkLeft).Information(
                        "{Name} left the {EventName} Tournament, group {Group} ({Reason})",
                        client.Character?.Name, ev.EventName, t.Group, reason);
            }
            return Task.CompletedTask;
        }

        public static bool IsTakingPart(Client client)
        {
            lock (Sync)
                return IsTakingPartLocked(client.ID);
        }

        private static bool IsTakingPartLocked(uint id) =>
            All.Any(ev => ev.tournaments.Any(t => t.Phase != Phase.Idle && t.Teams.Values.Any(e => e.Members.Contains(id))));

        public string StatusText()
        {
            lock (Sync)
                return $"{this.EventName}: " + string.Join("; ", this.tournaments.Select(t =>
                    $"{GroupNames[t.Group]} {t.Phase}, {t.Teams.Count} teams" +
                    (t.Bracket == null || t.Phase != Phase.Running ? "" :
                        $", round {t.Bracket.RoundNumber}, {t.Bracket.Alive.Count} left, {t.Matches.Count(m => !m.Over)} fighting"))) +
                    $". {this.rewards.Count(r => !r.Value.Claimed)} prizes unclaimed.";
        }

        // A sign-up taken back before the start; a leader's takes the team with it. Under the lock.
        private void Remove(Tournament t, uint id)
        {
            t.Returns.Remove(id);
            if (t.Teams.Remove(id))
                return;
            foreach (var entry in t.Teams.Values)
                entry.Members.Remove(id);
        }

        private static Match MatchOn(GameMap<Client> map) =>
            All.SelectMany(ev => ev.tournaments).SelectMany(t => t.Matches).FirstOrDefault(m => m.Map == map);

        private static TeamPk EventOf(Match match) =>
            All.First(ev => ev.tournaments.Any(t => t.Matches.Contains(match)));

        private static Entry SideOf(Match match, uint id) =>
            match.A.Members.Contains(id) ? match.A : match.B.Members.Contains(id) ? match.B : null;

        private static List<uint> MembersOf(Tournament t, uint leader)
        {
            lock (Sync)
                return t.Teams.TryGetValue(leader, out var e) ? e.Members.ToList() : new List<uint>();
        }

        private MsgTeamPKArenicScore ScoreOf(Match match) => new MsgTeamPKArenicScore(this.scoreType)
        {
            TeamA = match.A.Leader, NameA = match.A.Name, ScoreA = match.ScoreA,
            TeamB = match.B.Leader, NameB = match.B.Name, ScoreB = match.ScoreB,
        };

        private static (ushort X, ushort Y) WaitingSpot(MapGrid grid) =>
            Arena.Qualifier.Spot(grid, Random.Next(-6, 7));

        private async Task SendHomeAsync(Client client, Tournament t)
        {
            await client.SendAsync(new MsgTeamPKArenic(this.arenicType, ArenicAction.Close));
            MapPoint back;
            lock (Sync)
                back = t.Returns.GetValueOrDefault(client.ID);
            await EventHelpers.TeleportHomeAsync(client, back);
        }

        private async Task GroupNoticeAsync(Tournament t, string text)
        {
            var present = new List<Client>();
            lock (Sync)
                foreach (var id in t.Returns.Keys)
                    if (Kernel.Players.TryGet(id, out var player) && player.Character != null &&
                        (player.Map == t.WaitingRoom || t.Matches.Any(m => m.Map == player.Map)))
                        present.Add(player);
            var message = new MsgTalk(0, MsgTalk.TalkChannel.System, text);
            foreach (var player in present)
                await player.SendAsync(message);
        }

        private void Reject(Client client, string kind, string reason) =>
            LogEvents.For(typeof(TeamPk), LogEvents.TeamPkRejected).Information(
                "{Name} can't {Kind} in the {EventName} Tournament: {Reason}", client.Character?.Name, kind, this.EventName, reason);
    }
}

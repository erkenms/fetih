namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Events;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgWarFlag;

    // Capture the Flag, Saturday 21:00-22:00 by the client's Activity.ini, on map 2057. Guild
    // members enter at the Guild Controller into the safe zone, where nobody fights and the dead
    // revive (never where they lie). Each flag base belongs to the guild that dealt it the most
    // damage. Flags lie around the arena: touching one carries it (3 points, 3 exploits), and
    // carrying it into a base of the guild's within 60 seconds plants it (15 of each, double
    // while the base is X2). A killed carrier's flag goes to the killer, else it drops where the
    // carrier was. Killing another guild's member gives 1-3 points and exploits by battle power;
    // reviving a guild member 2 exploits, Stigma, Star of Accuracy, Magic Shield or Azure Shield
    // on one, or freeing one of Soul Shackle 1, and shackling an enemy 1. At the end the top 8
    // guilds get info.ini's [WarflagReward] CPs and silver into their funds.
    public static class CaptureTheFlag
    {
        public const string EventName = "Capture the Flag";
        public const uint MapID = 2057;
        public const uint FirstBaseID = 700001, FirstFlagID = 700101, LastFlagID = 700999;
        // ini/npc.ini NpcType868 FlagBase, NpcType891 Flag
        public const ushort BaseLook = 8680, FlagLook = 8910;
        public const byte FlagRole = 47;
        public const uint BaseLife = 10_000_000;
        public const int PickUpScore = 3, PlantScore = 15, ReviveExploits = 2, HelpExploits = 1;
        public const int Prized = 8;

        public static WarFlagArena Layout { get; set; }
        public static TimeSpan FlagTime { get; set; } = TimeSpan.FromSeconds(60);
        public static int FlagCount { get; set; } = 10;
        public static TimeSpan DoubleInterval { get; set; } = TimeSpan.FromMinutes(5);
        public static TimeSpan RankInterval { get; set; } = TimeSpan.FromSeconds(5);

        private sealed class FlagBase
        {
            public Npc Npc;
            public Area Area;
            public readonly Dictionary<uint, ulong> Damage = new Dictionary<uint, ulong>();
            public uint Occupier;
        }

        private sealed class Entrant
        {
            public uint GuildID;
            public string Name;
            public uint Exploits;
            public MapPoint Return;
            public bool Present;
        }

        private sealed class Carry
        {
            public DateTime Until;
            public ushort X, Y;
        }

        private static readonly object Sync = new object();
        private static bool IsRunning;
        private static readonly List<FlagBase> Bases = new List<FlagBase>();
        private static readonly Dictionary<uint, Npc> Flags = new Dictionary<uint, Npc>();
        private static readonly Dictionary<uint, Entrant> Entrants = new Dictionary<uint, Entrant>();
        private static readonly Dictionary<uint, Carry> Carriers = new Dictionary<uint, Carry>();
        private static readonly Dictionary<uint, (string Name, uint Points)> Scores = new Dictionary<uint, (string, uint)>();
        private static int DoubleBase = -1;
        private static uint NextFlagID = FirstFlagID;
        private static DateTime NextRankAt, NextDoubleAt;
        private static readonly Random Random = new Random();

        public static bool Running { get { lock (Sync) return IsRunning; } }

        public static void Subscribe() => EventScheduler.Subscribe(EventName, OnEdgeAsync);

        private static Task OnEdgeAsync(ScheduledEvent ev, EventWindow window, EventEdge edge, DateTime now) => edge switch
        {
            EventEdge.Starts => StartAsync(now),
            EventEdge.Ends => EndAsync(now),
            _ => Task.CompletedTask,
        };

        // A kill's points and exploits: 2 against a near equal, 3 against a stronger, 1 a weaker
        public static int KillScore(uint killerPower, uint victimPower) =>
            victimPower > killerPower + 5 ? 3 : victimPower + 5 >= killerPower ? 2 : 1;

        // Guilds by points, most first; equal points by name
        public static List<(uint GuildID, string Name, uint Points)> Ranking(IEnumerable<(uint GuildID, string Name, uint Points)> scores) =>
            scores.OrderByDescending(s => s.Points).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();

        public static async Task<bool> StartAsync(DateTime now)
        {
            lock (Sync)
            {
                if (IsRunning)
                    return false;
                if (Layout == null || Kernel.Maps?.Exists(MapID) != true || !Kernel.World.TryGet(MapID, out _))
                {
                    LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagFailed).Error(
                        "No Capture the Flag arena: map {MapId} or its client regions are missing", MapID);
                    return false;
                }
                IsRunning = true;
                Bases.Clear();
                Flags.Clear();
                Entrants.Clear();
                Carriers.Clear();
                Scores.Clear();
                DoubleBase = -1;
                NextFlagID = FirstFlagID;
                for (int i = 0; i < Layout.Bases.Count; i++)
                {
                    var (x, y) = Layout.Bases[i].Center;
                    Bases.Add(new FlagBase
                    {
                        Area = Layout.Bases[i],
                        Npc = new Npc { NpcID = FirstBaseID + (uint)i, Lookface = BaseLook, Role = Npc.PoleRole, MapID = MapID, X = x, Y = y, Life = BaseLife },
                    });
                }
                NextRankAt = now;
                NextDoubleAt = now + DoubleInterval;
            }
            await SpawnFlagsAsync();
            await BroadcastAsync("Capture the Flag has begun! Guild members can enter the arena through the Guild Controller in Twin City.");
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagStarted).Information(
                "Capture the Flag started with {BaseCount} flag bases", Layout.Bases.Count);
            return true;
        }

        // At the Guild Controller; null when in, else why not
        public static async Task<string> EnterAsync(Client client)
        {
            var character = client.Character;
            var membership = Guilds.Of(client.ID);
            string reason;
            lock (Sync)
                reason =
                    !IsRunning ? "not_running" :
                    membership == null ? "no_guild" :
                    client.Life.Dead ? "dead" :
                    character.MapID == MapID ? "inside" :
                    Tournaments.ElitePk.IsTakingPart(client) || Tournaments.TeamPk.IsTakingPart(client) ||
                    Comet.Game.Arena.Qualifier.IsSignedUp(client) ? "busy" :
                    null;
            if (reason != null)
            {
                Reject(client, "enter", reason);
                return reason;
            }
            var back = new MapPoint(character.MapID, character.X, character.Y);
            var (x, y) = SafeSpot();
            if (!await Movement.TeleportAsync(client, MapID, x, y))
            {
                Reject(client, "enter", "teleport");
                return "teleport";
            }
            lock (Sync)
            {
                if (!Entrants.TryGetValue(client.ID, out var entrant))
                    Entrants[client.ID] = entrant = new Entrant();
                entrant.GuildID = membership.Guild.ID;
                entrant.Name = character.Name;
                entrant.Return = back;
                entrant.Present = true;
                if (!Scores.ContainsKey(membership.Guild.ID))
                    Scores[membership.Guild.ID] = (membership.Guild.Name, 0);
            }
            await client.SendAsync(new MsgWarFlag(FlagAction.Enter));
            await client.SendAsync(FlagPositions());
            await client.SendAsync(ScoreRankFor(membership.Guild.ID));
            var x2 = DoublePacket();
            if (x2 != null)
                await client.SendAsync(x2);
            await Talk.NoticeAsync(client, "You entered the Capture the Flag arena. Occupy a flag base, then carry flags into it!");
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagEntered).Information(
                "{Name} of guild {Guild} entered Capture the Flag", character.Name, membership.Guild.Name);
            return null;
        }

        public static async Task HandleAsync(Client client, MsgWarFlag msg)
        {
            switch (msg.Action)
            {
                case FlagAction.PickedUp: await PickUpAsync(client, msg.Data); break;
                case FlagAction.Planted: await PlantAsync(client); break;
                case FlagAction.Running: await client.SendAsync(new MsgWarFlag(FlagAction.Running, Running ? 1u : 0u)); break;
                default:
                    LogEvents.For(typeof(CaptureTheFlag), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action}", msg.Type, msg.Action);
                    break;
            }
        }

        private static async Task PickUpAsync(Client client, uint flagID)
        {
            var character = client.Character;
            uint guild = 0;
            Npc flag = null;
            string reason;
            lock (Sync)
            {
                reason =
                    !IsRunning ? "not_running" :
                    character == null || character.MapID != MapID || !Entrants.TryGetValue(client.ID, out var entrant) ? "not_entered" :
                    client.Life.Dead ? "dead" :
                    Carriers.ContainsKey(client.ID) ? "carrying" :
                    !Flags.TryGetValue(flagID, out flag) ? "no_flag" :
                    MonsterAI.Distance(character.X, character.Y, flag.X, flag.Y) > 2 ? "too_far" :
                    null;
                if (reason == null)
                {
                    guild = Entrants[client.ID].GuildID;
                    Flags.Remove(flagID);
                    Carriers[client.ID] = new Carry { Until = DateTime.UtcNow + FlagTime, X = character.X, Y = character.Y };
                }
            }
            if (reason != null)
            {
                Reject(client, "pick_up", reason);
                return;
            }
            await SendAroundAsync(flag.X, flag.Y, MsgAction.RemoveEntity(flag.NpcID));
            AddScore(client.ID, guild, PickUpScore, PickUpScore);
            await Pking.SendStatusAsync(client);
            var effect = new MsgWarFlag(FlagAction.PickedUp, client.ID);
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await client.SendAsync(new MsgWarFlag(FlagAction.Countdown, (uint)FlagTime.TotalSeconds));
            await SendToArenaAsync(FlagPositions());
            await Talk.NoticeAsync(client, $"You took a flag! Plant it in your guild's flag base within {FlagTime.TotalSeconds:0} seconds.");
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagPickedUp).Information(
                "{Name} picked up flag {NpcId}", character.Name, flagID);
        }

        private static async Task PlantAsync(Client client)
        {
            var character = client.Character;
            uint guild = 0;
            bool doubled = false;
            string reason;
            lock (Sync)
            {
                int at = character == null ? -1 : Bases.FindIndex(b => b.Area.Contains(character.X, character.Y));
                reason =
                    !IsRunning ? "not_running" :
                    character == null || character.MapID != MapID || !Entrants.TryGetValue(client.ID, out var entrant) ? "not_entered" :
                    client.Life.Dead ? "dead" :
                    !Carriers.ContainsKey(client.ID) ? "no_flag" :
                    at < 0 ? "no_base" :
                    Bases[at].Occupier != entrant.GuildID ? "not_ours" :
                    null;
                if (reason == null)
                {
                    guild = Entrants[client.ID].GuildID;
                    doubled = at == DoubleBase;
                    Carriers.Remove(client.ID);
                }
            }
            if (reason != null)
            {
                Reject(client, "plant", reason);
                if (reason == "not_ours")
                    await Talk.NoticeAsync(client, "Your guild doesn't occupy this flag base.");
                return;
            }
            int score = doubled ? PlantScore * 2 : PlantScore;
            AddScore(client.ID, guild, score, PlantScore);
            await Pking.SendStatusAsync(client);
            var effect = new MsgWarFlag(FlagAction.Planted, client.ID);
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await SendToArenaAsync(new MsgTalk(0, MsgTalk.TalkChannel.System,
                $"{character.Name} planted a flag for {Guilds.Find(guild)?.Name}: {score} points!"));
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagPlanted).Information(
                "{Name} planted a flag for guild {GuildId}: {Points} points", character.Name, guild, score);
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagFailed).Error(e, "Capture the Flag loop failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now)
        {
            var drops = new List<(uint ID, ushort X, ushort Y, string Reason)>();
            var gone = new List<uint>();
            bool rank = false, x2 = false;
            lock (Sync)
            {
                if (!IsRunning)
                    return;
                foreach (var (id, entrant) in Entrants)
                {
                    if (!entrant.Present)
                        continue;
                    bool here = Kernel.Players.TryGet(id, out var p) && p.Character?.MapID == MapID;
                    if (!here)
                    {
                        entrant.Present = false;
                        gone.Add(id);
                    }
                    if (Carriers.TryGetValue(id, out var carry))
                    {
                        if (here)
                            (carry.X, carry.Y) = (p.Character.X, p.Character.Y);
                        if (!here || now >= carry.Until)
                        {
                            Carriers.Remove(id);
                            drops.Add((id, carry.X, carry.Y, here ? "time" : "left"));
                        }
                    }
                }
                if (now >= NextRankAt)
                {
                    rank = true;
                    NextRankAt = now + RankInterval;
                }
                if (now >= NextDoubleAt && Bases.Count > 0)
                {
                    x2 = true;
                    DoubleBase = Random.Next(Bases.Count);
                    NextDoubleAt = now + DoubleInterval;
                }
            }
            foreach (var drop in drops)
                await DropAsync(drop.ID, drop.X, drop.Y, drop.Reason);
            foreach (var id in gone)
                if (Kernel.Players.TryGet(id, out var player))
                    await player.SendAsync(new MsgWarFlag(FlagAction.Leave));
            await SpawnFlagsAsync();
            if (x2)
            {
                await SendToArenaAsync(DoublePacket());
                await SendToArenaAsync(new MsgTalk(0, MsgTalk.TalkChannel.System,
                    "A flag base is in the X2 state: flags planted there score double!"));
            }
            if (rank)
                await SendRanksAsync();
        }

        // The carrier lost the flag where they were
        private static async Task DropAsync(uint carrierID, ushort x, ushort y, string reason)
        {
            var flag = AddFlag(x, y);
            if (Kernel.Players.TryGet(carrierID, out var carrier) && carrier.Character != null)
            {
                await Pking.SendStatusAsync(carrier);
                if (reason == "time")
                    await Talk.NoticeAsync(carrier, "Time is up. You dropped the flag.");
            }
            if (flag != null)
                await ShowFlagAsync(flag);
            await SendToArenaAsync(FlagPositions());
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagDropped).Information(
                "Character {CharacterId} dropped a flag at {X},{Y} ({Reason})", carrierID, x, y, reason);
        }

        public static async Task<bool> EndAsync(DateTime now)
        {
            List<(uint GuildID, string Name, uint Points)> ranking;
            List<(uint ID, MapPoint Return)> inside;
            List<Entrant> entrants;
            lock (Sync)
            {
                if (!IsRunning)
                    return false;
                IsRunning = false;
                ranking = Ranking(Scores.Select(s => (s.Key, s.Value.Name, s.Value.Points)));
                inside = Entrants.Select(e => (e.Key, e.Value.Return)).ToList();
                entrants = Entrants.Values.ToList();
                Carriers.Clear();
                Flags.Clear();
                Bases.Clear();
                DoubleBase = -1;
            }

            var prizes = Layout?.Prizes ?? Array.Empty<(uint, ulong)>();
            for (int i = 0; i < Math.Min(Prized, ranking.Count); i++)
            {
                var guild = Guilds.Find(ranking[i].GuildID);
                if (guild == null || i >= prizes.Count || ranking[i].Points == 0)
                    continue;
                var (cps, silver) = prizes[i];
                guild.Fund += silver;
                guild.ConquerPointsFund = (uint)Math.Min(uint.MaxValue, (ulong)guild.ConquerPointsFund + cps);
                await GuildsRepository.SaveAsync(guild.ToRow());
                LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagRewarded).Information(
                    "Guild {Guild} ({GuildId}) ranked {Rank} in Capture the Flag with {Points} points: {ConquerPoints} CPs and {Silver} silver to its funds",
                    guild.Name, guild.ID, i + 1, ranking[i].Points, cps, silver);
            }

            foreach (var (id, back) in inside)
                if (Kernel.Players.TryGet(id, out var player))
                    Kernel.Post(player, async () =>
                    {
                        if (player.Character == null)
                            return;
                        await player.SendAsync(new MsgWarFlag(FlagAction.GameOver));
                        await player.SendAsync(new MsgWarFlag(FlagAction.Leave));
                        await Pking.SendStatusAsync(player);
                        if (player.Character.MapID != MapID)
                            return;
                        if (player.Life.Dead)
                            await Combat.Death.ReviveHereAsync(player, "capture_the_flag");
                        if (back == null || !await Movement.TeleportAsync(player, back.MapID, back.X, back.Y))
                            await Movement.TeleportAsync(player, StartPosition.MapID, StartPosition.X, StartPosition.Y);
                    });

            var top = ranking.Where(r => r.Points > 0).Take(3).Select((r, i) => $"{i + 1}. {r.Name} ({r.Points})").ToList();
            await BroadcastAsync(top.Count == 0
                ? "Capture the Flag is over."
                : "Capture the Flag is over! " + string.Join(", ", top) + ". The top 8 guilds' prizes went to their funds.");
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagEnded).Information(
                "Capture the Flag ended: {Ranking}; exploits {Exploits}",
                string.Join(", ", ranking.Select((r, i) => $"{i + 1}:{r.Name}={r.Points}")),
                string.Join(", ", entrants.Where(e => e.Exploits > 0).Select(e => $"{e.Name}={e.Exploits}")));
            return true;
        }

        // Combat asks before a hit on a player: null off the arena, else whether they fight
        public static bool? CanFight(Client attacker, Client target)
        {
            if (attacker.Character?.MapID != MapID)
                return null;
            lock (Sync)
            {
                if (!IsRunning || target.Character?.MapID != MapID || Layout == null ||
                    Layout.SafeZone.Contains(attacker.Character.X, attacker.Character.Y) ||
                    Layout.SafeZone.Contains(target.Character.X, target.Character.Y))
                    return false;
                return Entrants.TryGetValue(attacker.ID, out var a) && Entrants.TryGetValue(target.ID, out var b) &&
                    a.GuildID != b.GuildID;
            }
        }

        // Any death in the arena: the killer of another guild scores and takes the victim's flag
        public static async Task OnDeathAsync(Client victim, uint killerID)
        {
            var character = victim.Character;
            if (character == null || character.MapID != MapID)
                return;
            Client killer = null;
            bool taken = false, dropped = false;
            uint killerGuild = 0;
            int score = 0;
            lock (Sync)
            {
                if (!IsRunning || !Entrants.TryGetValue(victim.ID, out var v))
                    return;
                bool enemy = Kernel.Players.TryGet(killerID, out killer) && killer.Character?.MapID == MapID &&
                    Entrants.TryGetValue(killerID, out var k) && k.GuildID != v.GuildID && !killer.Life.Dead;
                if (enemy)
                {
                    killerGuild = Entrants[killerID].GuildID;
                    score = KillScore(Attributes.BattlePower(killer.Character), Attributes.BattlePower(character));
                }
                if (Carriers.Remove(victim.ID))
                {
                    if (enemy && !Carriers.ContainsKey(killerID))
                    {
                        Carriers[killerID] = new Carry { Until = DateTime.UtcNow + FlagTime, X = killer.Character.X, Y = killer.Character.Y };
                        taken = true;
                    }
                    else
                        dropped = true;
                }
            }
            if (dropped)
                await DropAsync(victim.ID, character.X, character.Y, "killed");
            if (score > 0)
            {
                AddScore(killerID, killerGuild, score, score);
                LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagKill).Information(
                    "{Killer} killed {Victim} in Capture the Flag: {Points} points", killer.Character.Name, character.Name, score);
            }
            if (taken)
            {
                await Pking.SendStatusAsync(victim);
                Kernel.Post(killer, async () =>
                {
                    await Pking.SendStatusAsync(killer);
                    var effect = new MsgWarFlag(FlagAction.PickedUp, killer.ID);
                    await killer.SendAsync(effect);
                    await Movement.SendToViewAsync(killer, effect);
                    await killer.SendAsync(new MsgWarFlag(FlagAction.Countdown, (uint)FlagTime.TotalSeconds));
                    await Talk.NoticeAsync(killer, $"You took {character.Name}'s flag! Plant it within {FlagTime.TotalSeconds:0} seconds.");
                });
            }
        }

        // Reviving (2) or helping (1) a guild member in the arena
        public static void OnHelp(Client caster, Client target, int exploits, string kind) =>
            Exploit(caster, target, sameGuild: true, exploits, kind);

        // Soul Shackle on another guild's member
        public static void OnShackle(Client caster, Client target) =>
            Exploit(caster, target, sameGuild: false, HelpExploits, "shackle");

        private static void Exploit(Client caster, Client target, bool sameGuild, int exploits, string kind)
        {
            if (caster == target || caster.Character?.MapID != MapID || target.Character?.MapID != MapID)
                return;
            lock (Sync)
            {
                if (!IsRunning || !Entrants.TryGetValue(caster.ID, out var a) || !Entrants.TryGetValue(target.ID, out var b) ||
                    (a.GuildID == b.GuildID) != sameGuild)
                    return;
                a.Exploits += (uint)exploits;
            }
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagExploit).Debug(
                "{Name} gained {Exploits} exploits in Capture the Flag ({Kind})", caster.Character.Name, exploits, kind);
        }

        // Where the dead in the arena revive: the safe zone
        public static MapPoint RevivePoint(Client client)
        {
            if (client.Character?.MapID != MapID || !Running)
                return null;
            var (x, y) = SafeSpot();
            return new MapPoint(MapID, x, y);
        }

        public static async Task LeaveAsync(Client client, string reason)
        {
            Carry carry = null;
            lock (Sync)
            {
                if (!IsRunning || !Entrants.TryGetValue(client.ID, out var entrant) || !entrant.Present)
                    return;
                entrant.Present = false;
                if (Carriers.Remove(client.ID, out carry) && client.Character != null)
                    (carry.X, carry.Y) = (client.Character.X, client.Character.Y);
            }
            if (carry != null)
                await DropAsync(client.ID, carry.X, carry.Y, reason);
        }

        // From the arena's envoy: back where the player entered from
        public static async Task ExitAsync(Client client)
        {
            MapPoint back;
            lock (Sync)
                back = Entrants.GetValueOrDefault(client.ID)?.Return;
            await LeaveAsync(client, "left");
            await client.SendAsync(new MsgWarFlag(FlagAction.Leave));
            if (back == null || !await Movement.TeleportAsync(client, back.MapID, back.X, back.Y))
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
        }

        public static UInt128 StatusOf(Client client)
        {
            lock (Sync)
                return Carriers.ContainsKey(client.ID) ? StatusFlags.CarryingFlag : UInt128.Zero;
        }

        public static uint ExploitsOf(uint characterID)
        {
            lock (Sync)
                return Entrants.TryGetValue(characterID, out var e) ? e.Exploits : 0;
        }

        public static uint PointsOf(uint guildID)
        {
            lock (Sync)
                return Scores.TryGetValue(guildID, out var s) ? s.Points : 0;
        }

        public static uint OccupierOf(int baseIndex)
        {
            lock (Sync)
                return baseIndex < Bases.Count ? Bases[baseIndex].Occupier : 0;
        }

        public static string StatusText()
        {
            lock (Sync)
            {
                if (!IsRunning)
                    return "Capture the Flag isn't running.";
                var top = Ranking(Scores.Select(s => (s.Key, s.Value.Name, s.Value.Points))).Take(8);
                return $"Capture the Flag: {Entrants.Count(e => e.Value.Present)} inside, {Flags.Count} flags, {Carriers.Count} carried. " +
                    "Bases: " + string.Join(", ", Bases.Select((b, i) => $"{i + 1}{(i == DoubleBase ? " X2" : "")} {Guilds.Find(b.Occupier)?.Name ?? "-"}")) +
                    ". Points: " + string.Join(", ", top.Select(t => $"{t.Name} {t.Points}"));
            }
        }

        // Flag bases, attacked like the Guild War's pole

        public static Npc FindBase(Client client, uint npcID)
        {
            if (client.Character?.MapID != MapID)
                return null;
            lock (Sync)
                return Bases.FirstOrDefault(b => b.Npc.NpcID == npcID)?.Npc;
        }

        public static IEnumerable<Npc> BasesInView(Client client)
        {
            var character = client.Character;
            if (character?.MapID != MapID)
                return Enumerable.Empty<Npc>();
            lock (Sync)
                return Bases.Select(b => b.Npc)
                    .Where(n => GameMap<Client>.InView(character.X, character.Y, n.X, n.Y) && ProblemLocked(client, n) == null)
                    .ToList();
        }

        public static string Problem(Client client, Npc flagBase)
        {
            lock (Sync)
                return ProblemLocked(client, flagBase);
        }

        private static string ProblemLocked(Client client, Npc flagBase)
        {
            var found = Bases.FirstOrDefault(b => b.Npc == flagBase);
            return
                !IsRunning || found == null ? "not_running" :
                !Entrants.TryGetValue(client.ID, out var entrant) ? "not_entered" :
                found.Occupier == entrant.GuildID ? "own_base" :
                null;
        }

        public static MsgNpcInfoEx BasePacket(Npc flagBase)
        {
            string name;
            lock (Sync)
            {
                var found = Bases.FirstOrDefault(b => b.Npc == flagBase);
                name = found == null || found.Occupier == 0 ? "FlagBase" : Guilds.Find(found.Occupier)?.Name ?? "FlagBase";
            }
            return new MsgNpcInfoEx(flagBase, BaseLife, BaseLife, BaseLook, name);
        }

        // The guild that dealt a base the most damage occupies it
        public static async Task AfterHitAsync(Client client, Npc flagBase, uint damage)
        {
            if (damage == 0)
                return;
            uint before, after;
            lock (Sync)
            {
                var found = Bases.FirstOrDefault(b => b.Npc == flagBase);
                if (!IsRunning || found == null || !Entrants.TryGetValue(client.ID, out var entrant) || found.Occupier == entrant.GuildID)
                    return;
                found.Damage[entrant.GuildID] = found.Damage.GetValueOrDefault(entrant.GuildID) + damage;
                before = found.Occupier;
                after = found.Damage.OrderByDescending(d => d.Value).First().Key;
                found.Occupier = after;
            }
            if (before == after)
                return;
            await SendAroundAsync(flagBase.X, flagBase.Y, BasePacket(flagBase));
            await SendRanksAsync();
            var name = Guilds.Find(after)?.Name;
            await SendToArenaAsync(new MsgTalk(0, MsgTalk.TalkChannel.System, $"{name} occupied a flag base!"));
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagOccupied).Information(
                "Guild {Guild} ({GuildId}) occupied flag base {NpcId}", name, after, flagBase.NpcID);
        }

        // Bases and flags coming into view; with a previous cell, only those that weren't in view there
        public static async Task ShowAsync(Client client, ushort x, ushort y, ushort? fromX = null, ushort? fromY = null)
        {
            if (client.Character?.MapID != MapID)
                return;
            List<Npc> bases, flags;
            lock (Sync)
            {
                if (!IsRunning)
                    return;
                bool New(Npc n) => GameMap<Client>.InView(x, y, n.X, n.Y) &&
                    !(fromX.HasValue && GameMap<Client>.InView(fromX.Value, fromY.Value, n.X, n.Y));
                bases = Bases.Select(b => b.Npc).Where(New).ToList();
                flags = Flags.Values.Where(New).ToList();
            }
            foreach (var b in bases)
                await client.SendAsync(BasePacket(b));
            foreach (var f in flags)
                await client.SendAsync(FlagPacket(f));
        }

        // The client asking for an NPC it heard of; true if it was a base or flag
        public static async Task<bool> QueryAsync(Client client, uint id)
        {
            if (client.Character?.MapID != MapID)
                return false;
            Npc flagBase, flag;
            lock (Sync)
            {
                flagBase = Bases.FirstOrDefault(b => b.Npc.NpcID == id)?.Npc;
                flag = Flags.GetValueOrDefault(id);
            }
            if (flagBase != null)
                await client.SendAsync(BasePacket(flagBase));
            else if (flag != null)
                await client.SendAsync(FlagPacket(flag));
            return flagBase != null || flag != null;
        }

        private static MsgNpcInfoEx FlagPacket(Npc flag) => new MsgNpcInfoEx(flag, 1, 1, FlagLook, null);

        private static void AddScore(uint characterID, uint guildID, int points, int exploits)
        {
            lock (Sync)
            {
                if (Scores.TryGetValue(guildID, out var s))
                    Scores[guildID] = (s.Name, s.Points + (uint)points);
                else
                    Scores[guildID] = (Guilds.Find(guildID)?.Name ?? guildID.ToString(), (uint)points);
                if (Entrants.TryGetValue(characterID, out var e))
                    e.Exploits += (uint)exploits;
            }
        }

        private static async Task SpawnFlagsAsync()
        {
            var added = new List<Npc>();
            lock (Sync)
            {
                if (!IsRunning || !Kernel.World.TryGet(MapID, out var map))
                    return;
                for (int tries = 0; Flags.Count < FlagCount && tries < FlagCount * 20; tries++)
                {
                    int x = Random.Next(map.Grid.Width), y = Random.Next(map.Grid.Height);
                    if (!map.Grid.IsWalkable(x, y) || Layout.SafeZone.Contains(x, y) || Bases.Any(b => b.Area.Contains(x, y)))
                        continue;
                    var flag = AddFlagLocked((ushort)x, (ushort)y);
                    if (flag != null)
                        added.Add(flag);
                }
            }
            foreach (var flag in added)
                await ShowFlagAsync(flag);
            if (added.Count > 0)
                await SendToArenaAsync(FlagPositions());
        }

        private static Npc AddFlag(ushort x, ushort y)
        {
            lock (Sync)
                return IsRunning ? AddFlagLocked(x, y) : null;
        }

        private static Npc AddFlagLocked(ushort x, ushort y)
        {
            for (int i = 0; i <= LastFlagID - FirstFlagID; i++)
            {
                uint id = NextFlagID;
                NextFlagID = NextFlagID >= LastFlagID ? FirstFlagID : NextFlagID + 1;
                if (Flags.ContainsKey(id))
                    continue;
                var flag = new Npc { NpcID = id, Lookface = FlagLook, Role = FlagRole, MapID = MapID, X = x, Y = y, Life = 1 };
                Flags[id] = flag;
                return flag;
            }
            return null;
        }

        private static Task ShowFlagAsync(Npc flag) => SendAroundAsync(flag.X, flag.Y, FlagPacket(flag));

        private static MsgWarFlag FlagPositions()
        {
            var msg = new MsgWarFlag(FlagAction.FlagPositions);
            lock (Sync)
                foreach (var flag in Flags.Values)
                    msg.Positions.Add((flag.X, flag.Y));
            return msg;
        }

        private static MsgWarFlag DoublePacket()
        {
            lock (Sync)
            {
                if (DoubleBase < 0 || DoubleBase >= Bases.Count)
                    return null;
                var npc = Bases[DoubleBase].Npc;
                var msg = new MsgWarFlag(FlagAction.DoubleScore, npc.NpcID);
                msg.Positions.Add((npc.X, npc.Y));
                return msg;
            }
        }

        // The guild ranking, with the reader's guild's bases for the radar
        private static MsgWarFlag ScoreRankFor(uint guildID)
        {
            var msg = new MsgWarFlag(FlagAction.ScoreRank) { GuildID = guildID };
            lock (Sync)
            {
                foreach (var r in Ranking(Scores.Select(s => (s.Key, s.Value.Name, s.Value.Points))).Take(Prized))
                    msg.Ranks.Add((r.Points, r.Name));
                foreach (var b in Bases.Where(b => b.Occupier == guildID).Take(3))
                    msg.GuildBases.Add((b.Npc.X, b.Npc.Y));
            }
            return msg;
        }

        private static async Task SendRanksAsync()
        {
            var readers = new List<(Client Player, uint GuildID, int Base)>();
            var damage = new Dictionary<int, MsgWarFlag>();
            lock (Sync)
            {
                foreach (var (id, entrant) in Entrants)
                    if (entrant.Present && Kernel.Players.TryGet(id, out var p) && p.Character?.MapID == MapID)
                        readers.Add((p, entrant.GuildID, Bases.FindIndex(b => b.Area.Contains(p.Character.X, p.Character.Y))));
                for (int i = 0; i < Bases.Count; i++)
                {
                    var msg = new MsgWarFlag(FlagAction.BaseDamageRank);
                    foreach (var d in Bases[i].Damage.OrderByDescending(d => d.Value).Take(Prized))
                        msg.Ranks.Add(((uint)Math.Min(uint.MaxValue, d.Value), Guilds.Find(d.Key)?.Name ?? d.Key.ToString()));
                    damage[i] = msg;
                }
            }
            var byGuild = new Dictionary<uint, MsgWarFlag>();
            foreach (var (player, guild, at) in readers)
            {
                if (!byGuild.TryGetValue(guild, out var rank))
                    byGuild[guild] = rank = ScoreRankFor(guild);
                await player.SendAsync(rank);
                if (at >= 0)
                    await player.SendAsync(damage[at]);
            }
        }

        private static (ushort X, ushort Y) SafeSpot()
        {
            var zone = Layout.SafeZone;
            if (Kernel.World.TryGet(MapID, out var map))
                for (int i = 0; i < 50; i++)
                {
                    int x = zone.X + Random.Next(zone.Width), y = zone.Y + Random.Next(zone.Height);
                    if (map.Grid.IsWalkable(x, y))
                        return ((ushort)x, (ushort)y);
                }
            return zone.Center;
        }

        private static async Task SendAroundAsync(ushort x, ushort y, MsgBase<Client> msg)
        {
            if (!Kernel.World.TryGet(MapID, out var map))
                return;
            foreach (var player in map.InView(x, y))
                await player.SendAsync(msg);
        }

        private static async Task SendToArenaAsync(MsgBase<Client> msg)
        {
            if (msg == null || !Kernel.World.TryGet(MapID, out var map))
                return;
            foreach (var player in map.All())
                if (player.Character != null)
                    await player.SendAsync(msg);
        }

        private static async Task BroadcastAsync(string text)
        {
            var message = new MsgTalk(0, MsgTalk.TalkChannel.Center, text);
            foreach (var player in Kernel.Players.Values.ToList())
                if (player.Character != null)
                    await player.SendAsync(message);
        }

        private static void Reject(Client client, string kind, string reason) =>
            LogEvents.For(typeof(CaptureTheFlag), LogEvents.CaptureTheFlagRejected).Information(
                "{Name} can't {Kind} in Capture the Flag: {Reason}", client.Character?.Name, kind, reason);
    }
}

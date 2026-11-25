namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;

    public sealed class BossSpawn
    {
        [JsonPropertyName("id")] public string ID { get; init; }
        [JsonPropertyName("monster")] public uint MonsterID { get; init; }
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("x")] public ushort X { get; init; }
        [JsonPropertyName("y")] public ushort Y { get; init; }
        [JsonPropertyName("spread")] public ushort Spread { get; init; }
        [JsonPropertyName("times")] public List<string> Times { get; init; } = new List<string>();
        [JsonPropertyName("intervalSeconds")] public int IntervalSeconds { get; init; }
        [JsonPropertyName("broadcast")] public string Broadcast { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        public TimeSpan[] DailyTimes { get; set; } = Array.Empty<TimeSpan>();

        private sealed record BossFile(List<BossSpawn> Bosses);

        public static List<BossSpawn> Load(string path, MonsterTypeTable types)
        {
            var bosses = DataFiles.Read<BossFile>(path).Bosses ?? new List<BossSpawn>();
            var empty = bosses.Where(b => string.IsNullOrWhiteSpace(b.ID)).ToList();
            if (empty.Count > 0)
                throw new InvalidDataException($"{path} has a boss entry without an id");

            var repeated = bosses.GroupBy(b => b.ID, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these boss IDs more than once: {string.Join(", ", repeated)}");

            var unknown = bosses.Where(b => !types.TryGet(b.MonsterID, out _)).Select(b => b.ID).ToList();
            if (unknown.Count > 0)
                throw new InvalidDataException($"{path} has bosses of unknown monsters: {string.Join(", ", unknown)}");

            foreach (var boss in bosses)
            {
                var parsed = new List<TimeSpan>();
                if (boss.Times != null)
                {
                    foreach (var raw in boss.Times)
                    {
                        if (!TimeSpan.TryParseExact(raw, @"hh\:mm", CultureInfo.InvariantCulture, out var time) ||
                            time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                            throw new InvalidDataException($"{path} has invalid daily time '{raw}' for boss {boss.ID}");
                        parsed.Add(time);
                    }
                }
                boss.DailyTimes = parsed.Distinct().OrderBy(t => t).ToArray();

                if (boss.DailyTimes.Length == 0 && boss.IntervalSeconds <= 0)
                    throw new InvalidDataException($"{path} boss {boss.ID} must have daily times or a positive intervalSeconds");
            }

            return bosses;
        }
    }

    public sealed class ScheduledBoss
    {
        public BossSpawn Config { get; }
        public Monster Monster { get; }
        public DateTime? NextIntervalAt { get; set; }
        public DateTime LastHandledDeath { get; set; }

        public ScheduledBoss(BossSpawn config, Monster monster, DateTime? nextIntervalAt)
        {
            this.Config = config;
            this.Monster = monster;
            this.NextIntervalAt = nextIntervalAt;
        }
    }

    public enum BossSpawnResult
    {
        Spawned,
        NotFound,
        AlreadyAlive,
        NoWalkableCell,
    }

    // Runs on the monster loop alongside Monsters.RespawnAsync. Each entry owns one Monster
    // instance with RespawnSeconds = -1 so only this schedule (or a GM command) brings it back.
    public sealed class BossSchedule
    {
        private readonly List<ScheduledBoss> Entries = new List<ScheduledBoss>();
        private readonly Dictionary<string, ScheduledBoss> ById = new Dictionary<string, ScheduledBoss>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<Monster, DateTime, Random, Task<bool>> Spawner;
        private readonly Func<string, Task> Announcer;
        private readonly Func<DateTime, DateTime> ToLocal;
        private DateTime? LastCheckedLocal;

        public BossSchedule() : this(Monsters.SpawnNowAsync, Chat.Talk.BroadcastAsync, Clock.Local) { }

        public BossSchedule(
            Func<Monster, DateTime, Random, Task<bool>> spawner,
            Func<string, Task> announcer,
            Func<DateTime, DateTime> toLocal = null)
        {
            this.Spawner = spawner;
            this.Announcer = announcer;
            this.ToLocal = toLocal ?? Clock.Local;
        }

        public IReadOnlyList<ScheduledBoss> All => this.Entries;
        public int Count => this.Entries.Count;

        public bool TryGet(string id, out ScheduledBoss entry) => this.ById.TryGetValue(id, out entry);

        public (int Registered, int Skipped) Register(IEnumerable<BossSpawn> spawns, DateTime now)
        {
            int registered = 0, skipped = 0;
            this.LastCheckedLocal = this.ToLocal(now);

            foreach (var spawn in spawns)
            {
                if (!Kernel.MonsterTypes.TryGet(spawn.MonsterID, out var type) ||
                    !Kernel.Monsters.TryGet(spawn.MapID, out var map))
                {
                    skipped++;
                    continue;
                }

                var generator = new Generator
                {
                    ID = 0,
                    MapID = spawn.MapID,
                    X = spawn.X,
                    Y = spawn.Y,
                    Spread = spawn.Spread,
                    Amount = 1,
                    RespawnSeconds = -1,
                    MonsterID = spawn.MonsterID,
                };
                var monster = Monsters.RegisterScheduled(type, generator, map);
                if (monster == null)
                {
                    skipped++;
                    continue;
                }

                this.AddEntry(spawn, monster, now);
                registered++;
            }

            return (registered, skipped);
        }

        // For unit tests that supply pre-built monsters without touching Kernel.Monsters
        public void AddEntry(BossSpawn spawn, Monster monster, DateTime now)
        {
            this.LastCheckedLocal ??= this.ToLocal(now);
            DateTime? nextInterval = spawn.IntervalSeconds > 0
                ? now + TimeSpan.FromSeconds(spawn.IntervalSeconds)
                : null;
            var entry = new ScheduledBoss(spawn, monster, nextInterval);
            this.Entries.Add(entry);
            this.ById[spawn.ID] = entry;
        }

        public async Task TickAsync(DateTime now, Random random)
        {
            if (this.Entries.Count == 0)
                return;

            var localNow = this.ToLocal(now);
            var previousLocal = this.LastCheckedLocal ?? localNow;
            this.LastCheckedLocal = localNow;

            foreach (var entry in this.Entries)
            {
                var monster = entry.Monster;
                if (monster.Alive)
                    continue;

                if (entry.Config.IntervalSeconds > 0 && monster.DiedAt != default && monster.DiedAt != entry.LastHandledDeath)
                {
                    entry.LastHandledDeath = monster.DiedAt;
                    entry.NextIntervalAt = monster.DiedAt + TimeSpan.FromSeconds(entry.Config.IntervalSeconds);
                }

                bool due = false;
                if (entry.Config.DailyTimes.Length > 0 && HasDailySlotInRange(entry.Config.DailyTimes, previousLocal, localNow))
                    due = true;
                if (entry.NextIntervalAt.HasValue && now >= entry.NextIntervalAt.Value)
                    due = true;

                if (!due)
                    continue;

                if (await this.SpawnEntryAsync(entry, now, random, manual: false) == BossSpawnResult.Spawned &&
                    entry.Config.IntervalSeconds > 0)
                    entry.NextIntervalAt = null;
            }
        }

        public async Task<BossSpawnResult> SpawnAsync(string id, DateTime now, Random random)
        {
            if (!this.ById.TryGetValue(id, out var entry))
                return BossSpawnResult.NotFound;
            var result = await this.SpawnEntryAsync(entry, now, random, manual: true);
            if (result == BossSpawnResult.Spawned && entry.Config.IntervalSeconds > 0)
                entry.NextIntervalAt = null;
            return result;
        }

        private async Task<BossSpawnResult> SpawnEntryAsync(ScheduledBoss entry, DateTime now, Random random, bool manual)
        {
            var monster = entry.Monster;
            if (monster.Alive)
                return BossSpawnResult.AlreadyAlive;
            if (!await this.Spawner(monster, now, random))
                return BossSpawnResult.NoWalkableCell;

            if (!string.IsNullOrWhiteSpace(entry.Config.Broadcast))
                await this.Announcer(entry.Config.Broadcast);

            LogEvents.For<BossSchedule>(LogEvents.BossSpawned).Information(
                "Boss {BossId} ({MonsterName} {MonsterId}) spawned on map {MapId} at {X},{Y} (manual={Manual})",
                entry.Config.ID, monster.Type.Name, monster.Identity, entry.Config.MapID, monster.X, monster.Y, manual);
            return BossSpawnResult.Spawned;
        }

        public static bool HasDailySlotInRange(TimeSpan[] dailyTimes, DateTime afterLocal, DateTime upToLocal)
        {
            if (dailyTimes.Length == 0 || upToLocal <= afterLocal)
                return false;
            for (var day = afterLocal.Date; day <= upToLocal.Date; day = day.AddDays(1))
            {
                foreach (var time in dailyTimes)
                {
                    var slot = day + time;
                    if (slot > afterLocal && slot <= upToLocal)
                        return true;
                }
            }
            return false;
        }

    }
}

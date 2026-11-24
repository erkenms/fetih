namespace Comet.Game.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using Comet.Game.Combat;
    using Comet.Game.Items;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    public enum DataProblemLevel { Error, Warning }

    // Entry is the index in the file's list and Line where it starts, when the problem has one
    public sealed record DataProblem(string File, int? Entry, int? Line, string Message,
        DataProblemLevel Level = DataProblemLevel.Error);

    // The Data directory's files, read when first needed, with the client's maps and items to
    // check them against. An edited file can stand in for the one on disk to validate it
    // before it's saved.
    public sealed class DataSet
    {
        private readonly Dictionary<string, DataFile> Files = new Dictionary<string, DataFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<uint, MapGrid> Grids;
        private readonly Func<uint, bool> ItemExists;

        public string Directory { get; }

        // grids gives null for a map GameMap.dat doesn't list; without it, maps aren't checked,
        // and without items, item IDs aren't
        public DataSet(string directory, Func<uint, MapGrid> grids = null, Func<uint, bool> items = null)
        {
            this.Directory = directory;
            this.Grids = grids;
            this.ItemExists = items;
        }

        public static DataSet ForClient(string directory, string clientDirectory)
        {
            var maps = new MapStore(clientDirectory);
            var items = ItemTypeTable.Load(clientDirectory);
            return new DataSet(directory, id => maps.Exists(id) ? maps.Get(id) : null, id => items.TryGet(id, out _));
        }

        public DataFile File(string fileName)
        {
            fileName = Path.GetFileName(fileName);
            if (!this.Files.TryGetValue(fileName, out var file))
            {
                var kind = DataFileKinds.Find(fileName) ?? throw new ArgumentException($"{fileName} isn't a known data file");
                file = kind.Load(Path.Combine(this.Directory, kind.FileName));
                this.Files[fileName] = file;
            }

            return file;
        }

        public void Use(string fileName, DataFile file) => this.Files[Path.GetFileName(fileName)] = file;

        public IReadOnlyList<DataProblem> ValidateAll() =>
            DataFileKinds.All.Where(k => System.IO.File.Exists(Path.Combine(this.Directory, k.FileName)) || this.Files.ContainsKey(k.FileName))
                .SelectMany(k => this.Validate(k.FileName)).ToList();

        public IReadOnlyList<DataProblem> Validate(string fileName)
        {
            var check = new Check(this, Path.GetFileName(fileName));
            try
            {
                check.Run();
            }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                check.Add(null, e.Message);
            }

            return check.Problems.Distinct().OrderBy(p => p.Line ?? 0).ToList();
        }

        private T[] Entries<T>(string fileName) where T : class => ((DataFile<T>)this.File(fileName)).All().ToArray();

        private sealed class Check
        {
            private readonly DataSet Set;
            private readonly string Name;
            private readonly Dictionary<uint, MapGrid> Grids = new Dictionary<uint, MapGrid>();
            private readonly Dictionary<uint, string> GridErrors = new Dictionary<uint, string>();
            private HashSet<uint> MonsterIDs;
            private DataFile File;

            public List<DataProblem> Problems { get; } = new List<DataProblem>();

            public Check(DataSet set, string name)
            {
                this.Set = set;
                this.Name = name;
            }

            public void Add(int? entry, string message, DataProblemLevel level = DataProblemLevel.Error) =>
                this.Problems.Add(new DataProblem(this.Name, entry,
                    entry is int i && this.File != null ? this.File.LineOf(i) : null, message, level));

            public void Run()
            {
                this.File = this.Set.File(this.Name);
                var kind = DataFileKinds.Find(this.Name);
                if (kind == DataFileKinds.Npcs || kind == DataFileKinds.Goals)
                    this.Npcs(kind == DataFileKinds.Npcs ? DataFileKinds.Goals : DataFileKinds.Npcs);
                else if (kind == DataFileKinds.Maps)
                    this.Maps();
                else if (kind == DataFileKinds.Portals)
                    this.Portals();
                else if (kind == DataFileKinds.Spawns)
                    this.Spawns();
                else if (kind == DataFileKinds.Monsters)
                    this.Monsters();
                else if (kind == DataFileKinds.Levels)
                    this.Levels();
                else if (kind == DataFileKinds.Talkers)
                    this.Talkers();
                else if (kind == DataFileKinds.Teleporters)
                    this.Teleporters();
                else if (kind == DataFileKinds.Bosses)
                    this.Bosses();
                else if (kind == DataFileKinds.Drops)
                    this.Drops();
            }

            private void Npcs(DataFileKind other)
            {
                var npcs = this.Set.Entries<Npc>(this.Name);
                this.Unique(npcs, n => n.NpcID, "NPC ID");
                if (System.IO.File.Exists(Path.Combine(this.Set.Directory, other.FileName)) || this.Set.Files.ContainsKey(other.FileName))
                {
                    var elsewhere = this.Set.Entries<Npc>(other.FileName).Select(n => n.NpcID).ToHashSet();
                    for (int i = 0; i < npcs.Length; i++)
                        if (elsewhere.Contains(npcs[i].NpcID))
                            this.Add(i, $"NPC ID {npcs[i].NpcID} is also in {other.FileName}");
                }

                for (int i = 0; i < npcs.Length; i++)
                    this.Cell(i, "The NPC", npcs[i].MapID, npcs[i].X, npcs[i].Y, walkable: false);
            }

            private void Maps()
            {
                var maps = this.Set.Entries<MapType>(this.Name);
                this.Unique(maps, m => m.MapID, "Map");
                var withPortal = maps.Where(m => m.Portal != null).Select(m => m.MapID).ToHashSet();
                var rebornTargets = maps.Where(m => m.RebornMapID != null).Select(m => m.RebornMapID.Value).ToHashSet();
                var names = maps.SelectMany((m, i) => m.Names.Select(n => (Name: n.ToLowerInvariant(), Index: i)))
                    .GroupBy(n => n.Name).Where(g => g.Count() > 1);
                foreach (var name in names)
                    foreach (var repeat in name.Skip(1))
                        this.Add(repeat.Index, $"The name '{name.Key}' is also used on line {this.File.LineOf(name.First().Index)}");
                this.Unique(maps, m => m.ScrollTypeID, "Scroll", m => m.ScrollTypeID != null);

                for (int i = 0; i < maps.Length; i++)
                {
                    var map = maps[i];
                    this.MapExists(i, map.MapID);
                    if (map.RebornMapID is uint reborn && !withPortal.Contains(reborn))
                        this.Add(i, $"Reborn map {reborn} has no portal in this file");
                    if (map.Flags.HasFlag(MapFlags.RecordDisable) && map.RebornMapID == null)
                        this.Add(i, "A RecordDisable map needs a reborn map");
                    if (map.Names.FirstOrDefault(n => uint.TryParse(n, out _)) is string number)
                        this.Add(i, $"The name '{number}' is a number");
                    if (map.ScrollTypeID != null && map.Portal == null)
                        this.Add(i, "A map with a scroll needs a portal");
                    if (map.ScrollTypeID is uint scroll)
                        this.Item(i, scroll, "Scroll");
                    // Many maps keep a portal point nobody is sent to; only a used one must be right
                    bool used = rebornTargets.Contains(map.MapID) || map.ScrollTypeID != null;
                    if (map.Portal != null)
                        this.Cell(i, "The portal point", map.MapID, map.Portal.X, map.Portal.Y, walkable: true,
                            used ? DataProblemLevel.Error : DataProblemLevel.Warning);
                }
            }

            private void Portals()
            {
                var portals = this.Set.Entries<Portal>(this.Name);
                this.Unique(portals, p => (p.MapID, p.PortalIndex), "Map and portal pair");
                for (int i = 0; i < portals.Length; i++)
                {
                    var portal = portals[i];
                    var grid = this.Grid(portal.MapID);
                    if (grid == null)
                        this.MapExists(i, portal.MapID);
                    else if (grid.Portals.All(p => p.Index != portal.PortalIndex))
                        this.Add(i, $"Map {portal.MapID} has no portal {portal.PortalIndex} " +
                            $"(it has {string.Join(", ", grid.Portals.Select(p => p.Index).OrderBy(x => x))})");
                    this.Cell(i, "The destination", portal.TargetMapID, portal.TargetX, portal.TargetY, walkable: true);
                }
            }

            private void Spawns()
            {
                var spawns = this.Set.Entries<Generator>(this.Name);
                this.Unique(spawns, s => s.ID, "Spawn ID");
                for (int i = 0; i < spawns.Length; i++)
                {
                    var spawn = spawns[i];
                    this.Monster(i, spawn.MonsterID);
                    if (spawn.Amount <= 0)
                        this.Add(i, "Amount is 0, so it spawns nothing", DataProblemLevel.Warning);
                    if (spawn.RespawnSeconds < 0)
                        this.Add(i, "Respawn can't be negative");
                    this.Cell(i, "The spawn's center", spawn.MapID, spawn.X, spawn.Y, walkable: spawn.Spread == 0);
                }
            }

            private void Monsters()
            {
                var monsters = this.Set.Entries<MonsterType>(this.Name);
                this.Unique(monsters, m => m.ID, "Monster ID");
                for (int i = 0; i < monsters.Length; i++)
                {
                    var monster = monsters[i];
                    if (monster.Life == 0)
                        this.Add(i, "Life must be above 0");
                    if (monster.AttackMin > monster.AttackMax)
                        this.Add(i, "attackMin is above attackMax");
                    // The potion a monster drops: one the client doesn't know never shows up
                    if (monster.DropHp != 0)
                        this.Item(i, monster.DropHp, "dropHp", DataProblemLevel.Warning);
                    if (monster.DropMp != 0)
                        this.Item(i, monster.DropMp, "dropMp", DataProblemLevel.Warning);
                }
            }

            private void Levels()
            {
                var levels = this.Set.Entries<LevelExp>(this.Name);
                this.Unique(levels, l => l.Level, "Level");
                for (int i = 0; i < levels.Length; i++)
                    if (levels[i].Experience == 0)
                        this.Add(i, "Experience must be above 0");
            }

            private void Talkers()
            {
                var talkers = this.Set.Entries<Talker>(this.Name);
                this.Unique(talkers, t => t.NpcID, "NPC ID");
                for (int i = 0; i < talkers.Length; i++)
                {
                    if (talkers[i].Text == null || talkers[i].Text.Count == 0)
                        this.Add(i, "No text");
                    if (string.IsNullOrEmpty(talkers[i].Reply))
                        this.Add(i, "No reply");
                }
            }

            private void Teleporters()
            {
                var teleporters = this.Set.Entries<Teleporter>(this.Name);
                this.Unique(teleporters, t => t.NpcID, "NPC ID");
                for (int i = 0; i < teleporters.Length; i++)
                {
                    var destinations = teleporters[i].Destinations ?? new List<Destination>();
                    if (destinations.Count == 0 || destinations.Count >= MsgTaskDialog.NoOption)
                        this.Add(i, $"Needs 1 to {MsgTaskDialog.NoOption - 1} destinations");
                    foreach (var to in destinations)
                    {
                        if (string.IsNullOrEmpty(to.Name))
                            this.Add(i, "A destination has no name");
                        this.Cell(i, $"Destination '{to.Name}'", to.MapID, to.X, to.Y, walkable: true);
                    }
                }
            }

            private void Bosses()
            {
                var bosses = this.Set.Entries<BossSpawn>(this.Name);
                for (int i = 0; i < bosses.Length; i++)
                    if (string.IsNullOrWhiteSpace(bosses[i].ID))
                        this.Add(i, "No id");
                this.Unique(bosses, b => b.ID?.ToLowerInvariant(), "Boss ID", b => !string.IsNullOrWhiteSpace(b.ID));
                for (int i = 0; i < bosses.Length; i++)
                {
                    var boss = bosses[i];
                    this.Monster(i, boss.MonsterID);
                    var times = boss.Times ?? new List<string>();
                    foreach (var raw in times)
                        if (!TimeSpan.TryParseExact(raw, @"hh\:mm", CultureInfo.InvariantCulture, out var time) ||
                            time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                            this.Add(i, $"'{raw}' isn't a time of day (HH:mm)");
                    if (times.Count == 0 && boss.IntervalSeconds <= 0)
                        this.Add(i, "Needs daily times or a positive intervalSeconds");
                    this.Cell(i, "The boss's spawn point", boss.MapID, boss.X, boss.Y, walkable: boss.Spread == 0);
                }
            }

            private void Drops()
            {
                var drops = this.Set.Entries<MonsterDrops>(this.Name);
                this.Unique(drops, d => d.MonsterID, "Monster");
                for (int i = 0; i < drops.Length; i++)
                {
                    this.Monster(i, drops[i].MonsterID);
                    foreach (var entry in drops[i].Entries ?? new List<DropEntry>())
                    {
                        this.Item(i, entry.ItemID, "Dropped item");
                        if (entry.Chance <= 0 || entry.Chance > 100)
                            this.Add(i, $"Chance {entry.Chance} for item {entry.ItemID} isn't in 0-100");
                        if (entry.Count < 1)
                            this.Add(i, $"Count {entry.Count} for item {entry.ItemID} must be at least 1");
                    }
                }
            }

            private void Unique<T, TKey>(T[] entries, Func<T, TKey> key, string what, Func<T, bool> where = null)
            {
                var first = new Dictionary<TKey, int>();
                for (int i = 0; i < entries.Length; i++)
                {
                    if (where != null && !where(entries[i]))
                        continue;
                    var k = key(entries[i]);
                    if (first.TryGetValue(k, out int earlier))
                        this.Add(i, $"{what} {k} is already used on line {this.File.LineOf(earlier)}");
                    else
                        first[k] = i;
                }
            }

            private void Monster(int entry, uint id)
            {
                this.MonsterIDs ??= this.Set.Entries<MonsterType>(DataFileKinds.Monsters.FileName).Select(m => m.ID).ToHashSet();
                if (!this.MonsterIDs.Contains(id))
                    this.Add(entry, $"Monster {id} isn't in {DataFileKinds.Monsters.FileName}");
            }

            private void Item(int entry, uint id, string what, DataProblemLevel level = DataProblemLevel.Error)
            {
                if (this.Set.ItemExists != null && !this.Set.ItemExists(id))
                    this.Add(entry, $"{what} {id} isn't in the client's itemtype.dat", level);
            }

            private void MapExists(int entry, uint mapID)
            {
                if (this.Set.Grids != null && this.Grid(mapID) == null)
                    this.Add(entry, this.GridErrors.TryGetValue(mapID, out var error) ? error : $"Map {mapID} isn't in the client's GameMap.dat");
            }

            // Off the map is an error; a blocked cell where a player is sent only a warning,
            // since the original data has some. NPCs stand on blocked cells.
            private void Cell(int entry, string what, uint mapID, int x, int y, bool walkable,
                DataProblemLevel level = DataProblemLevel.Error)
            {
                if (this.Set.Grids == null)
                    return;
                var grid = this.Grid(mapID);
                if (grid == null)
                    this.MapExists(entry, mapID);
                else if (!grid.Contains(x, y))
                    this.Add(entry, $"{what} ({x}, {y}) is outside map {mapID}, which is {grid.Width}x{grid.Height}", level);
                else if (walkable && !grid.IsWalkable(x, y))
                    this.Add(entry, $"{what} ({x}, {y}) on map {mapID} isn't walkable", DataProblemLevel.Warning);
            }

            private MapGrid Grid(uint mapID)
            {
                if (this.Set.Grids == null)
                    return null;
                if (!this.Grids.TryGetValue(mapID, out var grid) && !this.GridErrors.ContainsKey(mapID))
                {
                    try
                    {
                        grid = this.Set.Grids(mapID);
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException)
                    {
                        this.GridErrors[mapID] = $"Map {mapID} can't be read: {e.Message}";
                    }

                    this.Grids[mapID] = grid;
                }

                return grid;
            }
        }
    }
}

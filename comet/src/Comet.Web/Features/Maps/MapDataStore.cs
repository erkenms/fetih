namespace Comet.Web.Features.Maps;

using System.Collections.Concurrent;
using System.Text.Json;
using Comet.Game.Combat;
using Comet.Game.DataEditing;
using Comet.Game.Npcs;
using Comet.Game.Social;
using Comet.Game.World;
using Comet.Web.Features.Catalogs;

public sealed class MapDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? AssetsDir;
    private readonly string? DataDir;
    private readonly CatalogStore Catalogs;
    private readonly ClientFiles Client;
    private readonly ConcurrentDictionary<uint, MapGridResponse?> EncodedGrids = new();

    public MapDataStore(WebConfiguration config, CatalogStore catalogs, ClientFiles clientFiles)
        : this(
            WebConfiguration.PathsSection.Resolve(config.Paths.AssetsDirectory),
            StudioData.ResolveDataDirectory(config),
            catalogs,
            clientFiles)
    {
    }

    internal MapDataStore(string? assetsDir, string? dataDir, string? clientDir, CatalogStore catalogs)
        : this(assetsDir, dataDir, catalogs, new ClientFiles(clientDir))
    {
    }

    private MapDataStore(string? assetsDir, string? dataDir, CatalogStore catalogs, ClientFiles clientFiles)
    {
        this.AssetsDir = assetsDir;
        this.DataDir = dataDir;
        this.Catalogs = catalogs;
        this.Client = clientFiles;
    }

    public MapSummary[] ListMaps()
    {
        var data = this.ReadDataFiles();
        var byId = new SortedDictionary<uint, MapSummary>();

        foreach (var entry in this.Catalogs.Maps)
        {
            data.MapTypes.TryGetValue(entry.Id, out var mapType);
            byId[entry.Id] = this.BuildSummary(entry.Id, entry, mapType, data);
        }

        foreach (var (id, mapType) in data.MapTypes)
        {
            if (byId.ContainsKey(id))
                continue;
            byId[id] = this.BuildSummary(id, null, mapType, data);
        }

        return [.. byId.Values];
    }

    public MapDetail? GetMapDetail(uint mapId)
    {
        var data = this.ReadDataFiles();
        this.Catalogs.MapsById.TryGetValue(mapId, out var catalog);
        data.MapTypes.TryGetValue(mapId, out var mapType);
        var grid = this.Client.TryGetGrid(mapId);
        var meta = this.ReadTileMeta(mapId);

        if (catalog == null && mapType == null && grid == null && meta == null)
            return null;

        var facesById = this.Catalogs.Faces.Npc.ToDictionary(f => f.Id, f => f.Path);
        var npcNames = this.Client.NpcNames;

        var npcs = new List<MapNpcEntry>();
        var npcsById = new Dictionary<uint, Npc>();
        if (data.NpcsByMap.TryGetValue(mapId, out var mapNpcs))
        {
            foreach (var npc in mapNpcs)
            {
                npcsById[npc.NpcID] = npc;
                npcs.Add(BuildNpcEntry(npc, isGoal: false, data, npcNames, facesById));
            }
        }
        if (data.GoalsByMap.TryGetValue(mapId, out var mapGoals))
        {
            foreach (var goal in mapGoals)
            {
                npcs.Add(BuildNpcEntry(goal, isGoal: true, data, npcNames, facesById));
            }
        }

        var spawns = new List<MapSpawnEntry>();
        if (data.SpawnsByMap.TryGetValue(mapId, out var mapSpawns))
        {
            foreach (var s in mapSpawns)
            {
                this.Catalogs.MonstersById.TryGetValue(s.MonsterID, out var monster);
                spawns.Add(new MapSpawnEntry(
                    Id: s.ID,
                    X: s.X,
                    Y: s.Y,
                    Spread: s.Spread,
                    Amount: s.Amount,
                    Respawn: s.RespawnSeconds,
                    MonsterId: s.MonsterID,
                    MonsterName: monster?.Name,
                    MonsterLevel: monster?.Level ?? monster?.ClientLevel,
                    MonsterBoss: monster?.Boss ?? false));
            }
        }

        var portals = this.BuildPortals(mapId, grid, data);
        var teleporters = this.BuildTeleporters(npcsById, data, npcNames, facesById);
        var guildWar = mapId == GuildWar.MapID ? this.BuildGuildWar(npcsById) : null;

        int? width = grid?.Width ?? meta?.MapSize.Width;
        int? height = grid?.Height ?? meta?.MapSize.Height;

        return new MapDetail(
            Id: mapId,
            Name: catalog?.Name,
            ServerName: catalog?.ServerName ?? mapType?.Name,
            Aliases: catalog?.Aliases ?? mapType?.Aliases?.ToArray() ?? [],
            File: catalog?.File,
            Minimap: catalog?.Minimap,
            HasTiles: meta != null,
            HasGrid: grid != null,
            Width: width,
            Height: height,
            Flags: SplitFlags(mapType?.Flags),
            Portal: mapType?.Portal != null ? new MapCellPoint(mapType.Portal.X, mapType.Portal.Y) : null,
            RebornMap: mapType?.RebornMapID,
            RebornMapName: mapType?.RebornMapID is uint reborn ? this.MapDisplayName(reborn, data) : null,
            ScrollTypeId: mapType?.ScrollTypeID,
            RidePlus: mapType?.RidePlus ?? 0,
            Meta: meta,
            Npcs: [.. npcs.OrderBy(n => n.IsGoal).ThenBy(n => n.Id)],
            Spawns: [.. spawns.OrderBy(s => s.Id)],
            Portals: portals,
            Teleporters: teleporters,
            GuildWar: guildWar);
    }

    public MapGridResponse? GetMapGrid(uint mapId) =>
        this.EncodedGrids.GetOrAdd(mapId, id =>
        {
            var grid = this.Client.TryGetGrid(id);
            return grid == null ? null : EncodeGrid(id, grid);
        });

    public static MapGridResponse EncodeGrid(uint mapId, MapGrid grid)
    {
        int total = grid.Width * grid.Height;
        var walkableBytes = new byte[(total + 7) / 8];
        short minElev = short.MaxValue;
        short maxElev = short.MinValue;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                int idx = y * grid.Width + x;
                if (grid.IsWalkable(x, y))
                    walkableBytes[idx >> 3] |= (byte)(1 << (idx & 7));
                short e = grid.Elevation(x, y);
                if (e < minElev) minElev = e;
                if (e > maxElev) maxElev = e;
            }
        }

        if (total == 0)
        {
            minElev = 0;
            maxElev = 0;
        }

        var elevBytes = new byte[total];
        int range = maxElev - minElev;
        if (range > 0)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int idx = y * grid.Width + x;
                    elevBytes[idx] = (byte)Math.Clamp((grid.Elevation(x, y) - minElev) * 255 / range, 0, 255);
                }
            }
        }

        return new MapGridResponse(
            MapId: mapId,
            Width: grid.Width,
            Height: grid.Height,
            WalkableBase64: Convert.ToBase64String(walkableBytes),
            MinElevation: minElev,
            MaxElevation: maxElev,
            ElevationBase64: Convert.ToBase64String(elevBytes));
    }

    private MapSummary BuildSummary(uint id, MapCatalogEntry? catalog, MapType? mapType, DataSnapshot data)
    {
        int npcCount = (data.NpcsByMap.TryGetValue(id, out var n) ? n.Count : 0)
            + (data.GoalsByMap.TryGetValue(id, out var g) ? g.Count : 0);
        int spawnCount = data.SpawnsByMap.TryGetValue(id, out var s) ? s.Count : 0;
        int portalCount = data.PortalsByMap.TryGetValue(id, out var p) ? p.Count : 0;

        return new MapSummary(
            Id: id,
            Name: catalog?.Name,
            ServerName: catalog?.ServerName ?? mapType?.Name,
            Aliases: catalog?.Aliases ?? mapType?.Aliases?.ToArray() ?? [],
            File: catalog?.File,
            Minimap: catalog?.Minimap,
            HasTiles: this.HasTileMeta(id),
            Flags: SplitFlags(mapType?.Flags),
            Portal: mapType?.Portal != null ? new MapCellPoint(mapType.Portal.X, mapType.Portal.Y) : null,
            RebornMap: mapType?.RebornMapID,
            NpcCount: npcCount,
            SpawnCount: spawnCount,
            PortalCount: portalCount);
    }

    private MapPortalEntry[] BuildPortals(uint mapId, MapGrid? grid, DataSnapshot data)
    {
        var configured = data.PortalsByMap.TryGetValue(mapId, out var list)
            ? list.ToDictionary(p => p.PortalIndex)
            : new Dictionary<uint, Portal>();

        var result = new List<MapPortalEntry>();
        var seen = new HashSet<uint>();

        if (grid != null)
        {
            foreach (var p in grid.Portals)
            {
                seen.Add(p.Index);
                configured.TryGetValue(p.Index, out var target);
                result.Add(new MapPortalEntry(
                    Index: p.Index,
                    X: p.X,
                    Y: p.Y,
                    ToMap: target?.TargetMapID,
                    ToMapName: target != null ? this.MapDisplayName(target.TargetMapID, data) : null,
                    ToX: target?.TargetX,
                    ToY: target?.TargetY));
            }
        }

        foreach (var target in configured.Values.Where(t => !seen.Contains(t.PortalIndex)).OrderBy(t => t.PortalIndex))
        {
            result.Add(new MapPortalEntry(
                Index: target.PortalIndex,
                X: null,
                Y: null,
                ToMap: target.TargetMapID,
                ToMapName: this.MapDisplayName(target.TargetMapID, data),
                ToX: target.TargetX,
                ToY: target.TargetY));
        }

        return [.. result.OrderBy(p => p.Index)];
    }

    private MapTeleporterEntry[] BuildTeleporters(
        Dictionary<uint, Npc> npcsById,
        DataSnapshot data,
        IReadOnlyDictionary<int, string> npcNames,
        Dictionary<uint, string> facesById)
    {
        var result = new List<MapTeleporterEntry>();
        foreach (var tp in data.TeleportersByNpc.Values)
        {
            if (!npcsById.TryGetValue(tp.NpcID, out var npc))
                continue;

            facesById.TryGetValue(tp.Face, out var facePath);
            var destinations = (tp.Destinations ?? [])
                .Select(d => new MapDestinationEntry(
                    Name: d.Name,
                    MapId: d.MapID,
                    MapName: this.MapDisplayName(d.MapID, data),
                    X: d.X,
                    Y: d.Y))
                .ToArray();

            result.Add(new MapTeleporterEntry(
                NpcId: tp.NpcID,
                NpcName: ResolveNpcName(npc, npcNames),
                X: npc.X,
                Y: npc.Y,
                Fare: tp.Fare,
                Face: tp.Face,
                FacePath: facePath,
                Text: tp.Text?.ToArray() ?? [],
                Destinations: destinations));
        }

        return [.. result.OrderBy(t => t.NpcId)];
    }

    private MapGuildWarInfo BuildGuildWar(Dictionary<uint, Npc> npcsById)
    {
        var data = this.ReadDataFiles();

        MapGuildWarObject? Obj(uint id, string name, string role, uint defaultLife, MapCellPoint? inside = null) =>
            npcsById.TryGetValue(id, out var npc)
                ? new MapGuildWarObject(id, name, role, npc.X, npc.Y, npc.Life > 0 ? npc.Life : defaultLife, inside)
                : null;

        var conductors = GuildWarNpc.Conductors
            .Where(kv => npcsById.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var npc = npcsById[kv.Key];
                var (city, toMap, toX, toY) = kv.Value;
                return new MapGuildWarConductor(
                    NpcId: kv.Key,
                    City: city,
                    X: npc.X,
                    Y: npc.Y,
                    ToMap: toMap,
                    ToMapName: this.MapDisplayName(toMap, data),
                    ToX: toX,
                    ToY: toY);
            })
            .ToArray();

        return new MapGuildWarInfo(
            Pole: Obj(GuildWar.PoleID, GuildWar.DefaultPoleName, "pole", GuildWar.PoleMaxLife),
            LeftGate: Obj(GuildWar.LeftGateID, GuildWar.LeftGateName, "left_gate", GuildWar.GateMaxLife, new MapCellPoint(GuildWarNpc.LeftGateInside.X, GuildWarNpc.LeftGateInside.Y)),
            RightGate: Obj(GuildWar.RightGateID, GuildWar.RightGateName, "right_gate", GuildWar.GateMaxLife, new MapCellPoint(GuildWarNpc.RightGateInside.X, GuildWarNpc.RightGateInside.Y)),
            Conductors: conductors);
    }

    private static MapNpcEntry BuildNpcEntry(
        Npc npc,
        bool isGoal,
        DataSnapshot data,
        IReadOnlyDictionary<int, string> npcNames,
        Dictionary<uint, string> facesById)
    {
        string kind;
        byte? face = null;

        if (isGoal)
            kind = "goal";
        else if (npc.NpcID == GuildWar.PoleID)
            kind = "guild_pole";
        else if (npc.NpcID == GuildWar.LeftGateID || npc.NpcID == GuildWar.RightGateID)
            kind = "guild_gate";
        else if (GuildWarNpc.Conductors.ContainsKey(npc.NpcID))
            kind = "guild_conductor";
        else if (data.TeleportersByNpc.TryGetValue(npc.NpcID, out var tp))
        {
            kind = "teleporter";
            face = tp.Face;
        }
        else if (data.TalkerFacesByNpc.TryGetValue(npc.NpcID, out var talkerFace))
        {
            kind = "talker";
            face = talkerFace;
        }
        else
            kind = "npc";

        string? facePath = face is byte f && facesById.TryGetValue(f, out var path) ? path : null;
        return new MapNpcEntry(
            Id: npc.NpcID,
            Name: ResolveNpcName(npc, npcNames),
            Lookface: npc.Lookface,
            Direction: (byte)(npc.Lookface % 10),
            Role: npc.Role,
            X: npc.X,
            Y: npc.Y,
            Life: npc.Life,
            IsGoal: isGoal,
            FacePath: facePath,
            Kind: kind);
    }

    public string? NpcName(Npc npc) => ResolveNpcName(npc, this.Client.NpcNames);

    private static string? ResolveNpcName(Npc npc, IReadOnlyDictionary<int, string> npcNames)
    {
        if (npc.NpcID == GuildWar.PoleID) return GuildWar.DefaultPoleName;
        if (npc.NpcID == GuildWar.LeftGateID) return GuildWar.LeftGateName;
        if (npc.NpcID == GuildWar.RightGateID) return GuildWar.RightGateName;
        if (GuildWarNpc.Conductors.TryGetValue(npc.NpcID, out var c)) return $"GuildConductor ({c.City})";
        return npcNames.TryGetValue(npc.Lookface / 10, out var name) ? name : null;
    }

    private string? MapDisplayName(uint mapId, DataSnapshot data) =>
        this.Catalogs.MapDisplayName(mapId)
        ?? (data.MapTypes.TryGetValue(mapId, out var m) ? m.Name : null);

    private static string[] SplitFlags(MapFlags? flags)
    {
        if (flags is null or MapFlags.None)
            return [];
        return flags.Value.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private bool HasTileMeta(uint mapId) =>
        !string.IsNullOrEmpty(this.AssetsDir)
        && File.Exists(Path.Combine(this.AssetsDir, "maps", mapId.ToString(), "meta.json"));

    private MapTileMeta? ReadTileMeta(uint mapId)
    {
        if (string.IsNullOrEmpty(this.AssetsDir))
            return null;
        var path = Path.Combine(this.AssetsDir, "maps", mapId.ToString(), "meta.json");
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<MapTileMeta>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private DataSnapshot ReadDataFiles()
    {
        var mapTypes = new Dictionary<uint, MapType>();
        var npcsByMap = new Dictionary<uint, List<Npc>>();
        var goalsByMap = new Dictionary<uint, List<Npc>>();
        var spawnsByMap = new Dictionary<uint, List<Generator>>();
        var portalsByMap = new Dictionary<uint, List<Portal>>();
        var teleportersByNpc = new Dictionary<uint, Teleporter>();
        var talkerFacesByNpc = new Dictionary<uint, byte>();

        if (string.IsNullOrEmpty(this.DataDir) || !Directory.Exists(this.DataDir))
            return new DataSnapshot(mapTypes, npcsByMap, goalsByMap, spawnsByMap, portalsByMap, teleportersByNpc, talkerFacesByNpc);

        foreach (var m in ReadEntries<MapType>(DataFileKinds.Maps))
            mapTypes.TryAdd(m.MapID, m);

        foreach (var n in ReadEntries<Npc>(DataFileKinds.Npcs))
            AddTo(npcsByMap, n.MapID, n);

        foreach (var g in ReadEntries<Npc>(DataFileKinds.Goals))
            AddTo(goalsByMap, g.MapID, g);

        foreach (var s in ReadEntries<Generator>(DataFileKinds.Spawns))
            AddTo(spawnsByMap, s.MapID, s);

        foreach (var p in ReadEntries<Portal>(DataFileKinds.Portals))
            AddTo(portalsByMap, p.MapID, p);

        foreach (var t in ReadEntries<Teleporter>(DataFileKinds.Teleporters))
            teleportersByNpc.TryAdd(t.NpcID, t);

        foreach (var t in ReadEntries<Talker>(DataFileKinds.Talkers))
            if (t.Face > 0)
                talkerFacesByNpc.TryAdd(t.NpcID, t.Face);

        return new DataSnapshot(mapTypes, npcsByMap, goalsByMap, spawnsByMap, portalsByMap, teleportersByNpc, talkerFacesByNpc);

        IReadOnlyList<T> ReadEntries<T>(DataFileKind kind) where T : class
        {
            var path = Path.Combine(this.DataDir!, kind.FileName);
            if (!File.Exists(path))
                return [];
            try
            {
                return ((DataFile<T>)kind.Load(path)).All();
            }
            catch
            {
                return [];
            }
        }
    }

    private static void AddTo<T>(Dictionary<uint, List<T>> dict, uint key, T value)
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = [];
        list.Add(value);
    }

    private sealed record DataSnapshot(
        Dictionary<uint, MapType> MapTypes,
        Dictionary<uint, List<Npc>> NpcsByMap,
        Dictionary<uint, List<Npc>> GoalsByMap,
        Dictionary<uint, List<Generator>> SpawnsByMap,
        Dictionary<uint, List<Portal>> PortalsByMap,
        Dictionary<uint, Teleporter> TeleportersByNpc,
        Dictionary<uint, byte> TalkerFacesByNpc);
}

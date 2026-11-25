namespace Comet.Web.Features.MapEdit;

using Comet.Game.DataEditing;
using Comet.Game.World;
using Comet.Web.Audit;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.Maps;

public sealed class MapEditService
{
    public static readonly string[] AllFlags = Enum.GetNames<MapFlags>()
        .Where(n => n != nameof(MapFlags.None))
        .ToArray();

    public static readonly string[] MapDataFiles =
    [
        DataFileKinds.Maps.FileName,
        DataFileKinds.Npcs.FileName,
        DataFileKinds.Spawns.FileName,
        DataFileKinds.Portals.FileName,
    ];

    private readonly WebConfiguration Config;
    private readonly CatalogStore Catalogs;
    private readonly MapDataStore MapData;
    private readonly ClientFiles Client;
    private readonly SemaphoreSlim WriteGate = new(1, 1);

    public MapEditService(WebConfiguration config, CatalogStore catalogs, MapDataStore mapData, ClientFiles client)
    {
        this.Config = config;
        this.Catalogs = catalogs;
        this.MapData = mapData;
        this.Client = client;
    }

    public MapEditResponse ReadMap(uint mapId)
    {
        var dataDir = this.ResolveDataDirectory();
        var set = this.CreateDataSet(dataDir);

        var mapsFile = (DataFile<MapType>)set.File(DataFileKinds.Maps.FileName);
        var npcsFile = (DataFile<Npc>)set.File(DataFileKinds.Npcs.FileName);
        var spawnsFile = (DataFile<Generator>)set.File(DataFileKinds.Spawns.FileName);
        var portalsFile = (DataFile<Portal>)set.File(DataFileKinds.Portals.FileName);

        var mapType = mapsFile.All().FirstOrDefault(m => m.MapID == mapId);
        var settings = new EditableMapSettings(
            InMapsJson: mapType != null,
            Name: mapType?.Name,
            Aliases: mapType?.Aliases?.ToArray() ?? [],
            Flags: SplitFlags(mapType?.Flags),
            Portal: mapType?.Portal != null ? new MapCellPoint(mapType.Portal.X, mapType.Portal.Y) : null,
            RebornMap: mapType?.RebornMapID,
            ScrollTypeId: mapType?.ScrollTypeID,
            RidePlus: mapType?.RidePlus ?? 0);

        uint maxNpcId = 0;
        var mapNpcs = new List<EditableNpc>();
        for (int i = 0; i < npcsFile.Count; i++)
        {
            var n = npcsFile[i];
            if (n.NpcID > maxNpcId) maxNpcId = n.NpcID;
            if (n.MapID != mapId) continue;
            mapNpcs.Add(new EditableNpc(n.NpcID, n.Lookface, n.Role, n.X, n.Y, n.Life, n.Level, this.MapData.NpcName(n)));
        }

        // Also avoid colliding with training goals in goals.json when suggesting the next NPC ID
        var goalsPath = Path.Combine(dataDir, DataFileKinds.Goals.FileName);
        if (File.Exists(goalsPath))
        {
            try
            {
                var goalsFile = (DataFile<Npc>)set.File(DataFileKinds.Goals.FileName);
                for (int i = 0; i < goalsFile.Count; i++)
                {
                    if (goalsFile[i].NpcID > maxNpcId) maxNpcId = goalsFile[i].NpcID;
                }
            }
            catch { /* Ignore unreadable goals.json */ }
        }

        uint maxSpawnId = 0;
        var mapSpawns = new List<EditableSpawn>();
        for (int i = 0; i < spawnsFile.Count; i++)
        {
            var s = spawnsFile[i];
            if (s.ID > maxSpawnId) maxSpawnId = s.ID;
            if (s.MapID != mapId) continue;
            this.Catalogs.MonstersById.TryGetValue(s.MonsterID, out var monster);
            mapSpawns.Add(new EditableSpawn(
                s.ID,
                s.X,
                s.Y,
                s.Spread,
                s.Amount,
                s.RespawnSeconds,
                s.MonsterID,
                monster?.Name,
                monster?.Level ?? monster?.ClientLevel,
                monster?.Boss ?? false));
        }

        var mapNames = mapsFile.All().ToDictionary(m => m.MapID, m => m.Name);
        var configuredPortals = portalsFile.All()
            .Where(p => p.MapID == mapId)
            .GroupBy(p => p.PortalIndex)
            .ToDictionary(g => g.Key, g => g.First());

        var mapPortals = new List<EditablePortal>();
        var seenPortals = new HashSet<uint>();
        var grid = this.Client.TryGetGrid(mapId);
        if (grid != null)
        {
            foreach (var p in grid.Portals)
            {
                seenPortals.Add(p.Index);
                configuredPortals.TryGetValue(p.Index, out var target);
                mapPortals.Add(new EditablePortal(
                    p.Index,
                    p.X,
                    p.Y,
                    target?.TargetMapID,
                    target != null ? this.ResolveMapName(target.TargetMapID, mapNames) : null,
                    target?.TargetX,
                    target?.TargetY));
            }
        }

        foreach (var target in configuredPortals.Values.Where(p => !seenPortals.Contains(p.PortalIndex)).OrderBy(p => p.PortalIndex))
        {
            mapPortals.Add(new EditablePortal(
                target.PortalIndex,
                null,
                null,
                target.TargetMapID,
                this.ResolveMapName(target.TargetMapID, mapNames),
                target.TargetX,
                target.TargetY));
        }

        var hashes = new MapEditHashes(mapsFile.Hash, npcsFile.Hash, spawnsFile.Hash, portalsFile.Hash);
        var problems = CollectMapProblems(set, mapId, mapsFile, npcsFile, spawnsFile, portalsFile);

        return new MapEditResponse(
            MapId: mapId,
            Hashes: hashes,
            Settings: settings,
            AvailableFlags: AllFlags,
            Npcs: [.. mapNpcs.OrderBy(n => n.Id)],
            Spawns: [.. mapSpawns.OrderBy(s => s.Id)],
            Portals: [.. mapPortals.OrderBy(p => p.Index)],
            NextNpcId: maxNpcId + 1,
            NextSpawnId: maxSpawnId + 1,
            Problems: problems);
    }

    public async Task<SaveMapEditResponse> SaveMapAsync(
        uint mapId,
        SaveMapEditRequest request,
        AuditLog audit,
        CancellationToken ct)
    {
        await this.WriteGate.WaitAsync(ct);
        try
        {
            var dataDir = this.ResolveDataDirectory();
            var mapsFile = DataFile<MapType>.Load(Path.Combine(dataDir, DataFileKinds.Maps.FileName), DataFileKinds.Maps.ArrayKey);
            var npcsFile = DataFile<Npc>.Load(Path.Combine(dataDir, DataFileKinds.Npcs.FileName), DataFileKinds.Npcs.ArrayKey);
            var spawnsFile = DataFile<Generator>.Load(Path.Combine(dataDir, DataFileKinds.Spawns.FileName), DataFileKinds.Spawns.ArrayKey);
            var portalsFile = DataFile<Portal>.Load(Path.Combine(dataDir, DataFileKinds.Portals.FileName), DataFileKinds.Portals.ArrayKey);

            mapsFile.EnsureHash(request.Hashes.Maps);
            npcsFile.EnsureHash(request.Hashes.Npcs);
            spawnsFile.EnsureHash(request.Hashes.Spawns);
            portalsFile.EnsureHash(request.Hashes.Portals);

            var preProblems = new List<MapEditProblemDto>();
            var parsedFlags = ParseFlags(request.Settings.Flags, preProblems);

            // 1. Patch maps.json for this map
            bool mapsChanged = ApplyMapSettings(mapsFile, mapId, request.Settings, parsedFlags);

            // 2. Patch npcs.json for this map
            var nextNpcs = request.Npcs.Select(n => new Npc
            {
                NpcID = n.Id,
                Lookface = n.Lookface,
                Role = n.Role,
                MapID = mapId,
                X = n.X,
                Y = n.Y,
                Life = n.Life,
                Level = n.Level,
            }).ToList();
            var (npcsAdded, npcsUpdated, npcsRemoved) = ApplyScopedEntries(
                npcsFile,
                n => n.MapID == mapId,
                nextNpcs,
                n => n.NpcID,
                Npc.Same);

            // 3. Patch spawns.json for this map
            var nextSpawns = request.Spawns.Select(s => new Generator
            {
                ID = s.Id,
                MapID = mapId,
                X = s.X,
                Y = s.Y,
                Spread = s.Spread,
                Amount = s.Amount,
                RespawnSeconds = s.Respawn,
                MonsterID = s.MonsterId,
            }).ToList();
            var (spawnsAdded, spawnsUpdated, spawnsRemoved) = ApplyScopedEntries(
                spawnsFile,
                s => s.MapID == mapId,
                nextSpawns,
                s => s.ID,
                Generator.Same);

            // 4. Patch portals.json for this map (only entries with a target map/cell)
            var nextPortals = request.Portals
                .Where(p => p.ToMap.HasValue && p.ToX.HasValue && p.ToY.HasValue)
                .Select(p => new Portal
                {
                    MapID = mapId,
                    PortalIndex = p.Index,
                    TargetMapID = p.ToMap!.Value,
                    TargetX = p.ToX!.Value,
                    TargetY = p.ToY!.Value,
                }).ToList();
            var (portalsAdded, portalsUpdated, portalsRemoved) = ApplyScopedEntries(
                portalsFile,
                p => p.MapID == mapId,
                nextPortals,
                p => p.PortalIndex,
                PortalSame);

            var counts = new MapEditChangeCounts(
                mapsChanged,
                npcsAdded, npcsUpdated, npcsRemoved,
                spawnsAdded, spawnsUpdated, spawnsRemoved,
                portalsAdded, portalsUpdated, portalsRemoved);

            var set = this.CreateDataSet(dataDir);
            set.Use(DataFileKinds.Maps.FileName, mapsFile);
            set.Use(DataFileKinds.Npcs.FileName, npcsFile);
            set.Use(DataFileKinds.Spawns.FileName, spawnsFile);
            set.Use(DataFileKinds.Portals.FileName, portalsFile);

            var allProblems = new List<MapEditProblemDto>(preProblems);
            var changedFiles = new List<(string Name, DataFile File)>();
            if (mapsFile.IsChanged) changedFiles.Add((DataFileKinds.Maps.FileName, mapsFile));
            if (npcsFile.IsChanged) changedFiles.Add((DataFileKinds.Npcs.FileName, npcsFile));
            if (spawnsFile.IsChanged) changedFiles.Add((DataFileKinds.Spawns.FileName, spawnsFile));
            if (portalsFile.IsChanged) changedFiles.Add((DataFileKinds.Portals.FileName, portalsFile));

            foreach (var (name, _) in changedFiles)
            {
                allProblems.AddRange(ToDtos(set.Validate(name)));
            }

            bool hasErrors = allProblems.Any(p => p.Level == "error");
            if (hasErrors)
            {
                return new SaveMapEditResponse(
                    Saved: false,
                    Hashes: new MapEditHashes(mapsFile.Hash, npcsFile.Hash, spawnsFile.Hash, portalsFile.Hash),
                    ChangedFiles: [],
                    Problems: [.. allProblems],
                    Counts: counts);
            }

            foreach (var (_, file) in changedFiles)
                file.Save();

            var changedNames = changedFiles.Select(f => f.Name).ToArray();
            if (changedNames.Length > 0)
            {
                await audit.WriteAsync(
                    "map.save",
                    "map",
                    mapId.ToString(),
                    after: new { mapId, changedFiles = changedNames, counts },
                    summary: BuildAuditSummary(mapId, counts));
            }

            return new SaveMapEditResponse(
                Saved: true,
                Hashes: new MapEditHashes(mapsFile.Hash, npcsFile.Hash, spawnsFile.Hash, portalsFile.Hash),
                ChangedFiles: changedNames,
                Problems: [.. allProblems],
                Counts: counts);
        }
        finally
        {
            this.WriteGate.Release();
        }
    }

    // Patches only entries matching `inScope` in place by key so other maps and unchanged entries keep
    // their exact formatting and comments.
    public static (int Added, int Updated, int Removed) ApplyScopedEntries<T, TKey>(
        DataFile<T> file,
        Func<T, bool> inScope,
        IReadOnlyList<T> next,
        Func<T, TKey> keyOf,
        Func<T, T, bool> same)
        where T : class
        where TKey : notnull
    {
        var nextByKey = new Dictionary<TKey, Queue<int>>();
        for (int i = 0; i < next.Count; i++)
        {
            var k = keyOf(next[i]);
            if (!nextByKey.TryGetValue(k, out var q))
            {
                q = new Queue<int>();
                nextByKey[k] = q;
            }
            q.Enqueue(i);
        }

        var matchedNext = new bool[next.Count];
        var scopeIndices = new List<(int FileIndex, int NextIndex)>();
        for (int i = 0; i < file.Count; i++)
        {
            var current = file[i];
            if (!inScope(current))
                continue;
            var k = keyOf(current);
            if (nextByKey.TryGetValue(k, out var q) && q.Count > 0)
            {
                int nextIdx = q.Dequeue();
                scopeIndices.Add((i, nextIdx));
                matchedNext[nextIdx] = true;
            }
            else
            {
                scopeIndices.Add((i, -1));
            }
        }

        int updated = 0;
        int removed = 0;
        for (int s = scopeIndices.Count - 1; s >= 0; s--)
        {
            var (fileIdx, nextIdx) = scopeIndices[s];
            if (nextIdx < 0)
            {
                file.RemoveAt(fileIdx);
                removed++;
            }
            else
            {
                var target = next[nextIdx];
                if (!same(file[fileIdx], target))
                {
                    file[fileIdx] = target;
                    updated++;
                }
            }
        }

        int added = 0;
        for (int i = 0; i < next.Count; i++)
        {
            if (!matchedNext[i])
            {
                file.Add(next[i]);
                added++;
            }
        }

        return (added, updated, removed);
    }

    private static bool ApplyMapSettings(
        DataFile<MapType> file,
        uint mapId,
        EditableMapSettings settings,
        MapFlags flags)
    {
        string? name = string.IsNullOrWhiteSpace(settings.Name) ? null : settings.Name.Trim();
        var aliases = settings.Aliases?
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (aliases is { Count: 0 }) aliases = null;

        var next = new MapType
        {
            MapID = mapId,
            Name = name,
            Aliases = aliases,
            Flags = flags,
            Portal = settings.Portal != null ? new Cell(settings.Portal.X, settings.Portal.Y) : null,
            RebornMapID = settings.RebornMap,
            ScrollTypeID = settings.ScrollTypeId,
            RidePlus = settings.RidePlus,
        };

        int existingIndex = -1;
        for (int i = 0; i < file.Count; i++)
        {
            if (file[i].MapID == mapId)
            {
                existingIndex = i;
                break;
            }
        }

        bool hasAnyValue = name != null
            || aliases != null
            || flags != MapFlags.None
            || next.Portal != null
            || next.RebornMapID != null
            || next.ScrollTypeID != null
            || next.RidePlus > 0;

        if (existingIndex >= 0)
        {
            if (MapTypeSame(file[existingIndex], next))
                return false;
            file[existingIndex] = next;
            return true;
        }

        if (!hasAnyValue && !settings.InMapsJson)
            return false;

        file.Add(next);
        return true;
    }

    private static bool MapTypeSame(MapType a, MapType b)
    {
        if (a.MapID != b.MapID
            || a.Name != b.Name
            || a.Flags != b.Flags
            || a.Portal != b.Portal
            || a.RebornMapID != b.RebornMapID
            || a.ScrollTypeID != b.ScrollTypeID
            || a.RidePlus != b.RidePlus)
            return false;
        var aa = a.Aliases ?? [];
        var ba = b.Aliases ?? [];
        return aa.Count == ba.Count && aa.SequenceEqual(ba);
    }

    private static bool PortalSame(Portal a, Portal b) =>
        a.MapID == b.MapID
        && a.PortalIndex == b.PortalIndex
        && a.TargetMapID == b.TargetMapID
        && a.TargetX == b.TargetX
        && a.TargetY == b.TargetY;

    private static MapFlags ParseFlags(string[]? flags, List<MapEditProblemDto> problems)
    {
        MapFlags result = MapFlags.None;
        if (flags == null) return result;
        foreach (var raw in flags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (Enum.TryParse<MapFlags>(raw.Trim(), ignoreCase: true, out var parsed))
                result |= parsed;
            else
                problems.Add(new MapEditProblemDto(DataFileKinds.Maps.FileName, null, null, $"Unknown map flag '{raw}'", "error"));
        }
        return result;
    }

    private static string[] SplitFlags(MapFlags? flags)
    {
        if (flags is null or MapFlags.None) return [];
        return flags.Value.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static MapEditProblemDto[] CollectMapProblems(
        DataSet set,
        uint mapId,
        DataFile<MapType> mapsFile,
        DataFile<Npc> npcsFile,
        DataFile<Generator> spawnsFile,
        DataFile<Portal> portalsFile)
    {
        var result = new List<MapEditProblemDto>();

        void AddScoped<T>(string fileName, DataFile<T> file, Func<T, uint> mapOf) where T : class
        {
            foreach (var p in set.Validate(fileName))
            {
                if (p.Entry is int idx && idx >= 0 && idx < file.Count && mapOf(file[idx]) == mapId)
                    result.Add(ToDto(p));
            }
        }

        AddScoped(DataFileKinds.Maps.FileName, mapsFile, m => m.MapID);
        AddScoped(DataFileKinds.Npcs.FileName, npcsFile, n => n.MapID);
        AddScoped(DataFileKinds.Spawns.FileName, spawnsFile, s => s.MapID);
        AddScoped(DataFileKinds.Portals.FileName, portalsFile, p => p.MapID);
        return [.. result];
    }

    private static string BuildAuditSummary(uint mapId, MapEditChangeCounts c)
    {
        var parts = new List<string>();
        if (c.MapsChanged) parts.Add("settings");
        if (c.NpcsAdded + c.NpcsUpdated + c.NpcsRemoved > 0)
            parts.Add($"npcs +{c.NpcsAdded}/~{c.NpcsUpdated}/-{c.NpcsRemoved}");
        if (c.SpawnsAdded + c.SpawnsUpdated + c.SpawnsRemoved > 0)
            parts.Add($"spawns +{c.SpawnsAdded}/~{c.SpawnsUpdated}/-{c.SpawnsRemoved}");
        if (c.PortalsAdded + c.PortalsUpdated + c.PortalsRemoved > 0)
            parts.Add($"portals +{c.PortalsAdded}/~{c.PortalsUpdated}/-{c.PortalsRemoved}");
        return $"Saved map {mapId}: {string.Join(", ", parts)}";
    }

    private string? ResolveMapName(uint mapId, Dictionary<uint, string> serverNames)
    {
        if (this.Catalogs.MapsById.TryGetValue(mapId, out var c))
            return c.Name ?? c.ServerName;
        return serverNames.TryGetValue(mapId, out var name) ? name : null;
    }

    private string ResolveDataDirectory() => StudioData.ResolveDataDirectory(this.Config);

    private DataSet CreateDataSet(string dataDir) => this.Client.CreateDataSet(dataDir);

    private static MapEditProblemDto ToDto(DataProblem p) =>
        new(p.File, p.Entry, p.Line, p.Message, p.Level == DataProblemLevel.Warning ? "warning" : "error");

    private static MapEditProblemDto[] ToDtos(IReadOnlyList<DataProblem> problems) =>
        problems.Select(ToDto).ToArray();
}

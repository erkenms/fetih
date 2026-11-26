namespace Fetih.Web.Features.DataEdit;

using Fetih.Game.DataEditing;
using Fetih.Game.Npcs;
using Fetih.Game.States;
using Fetih.Game.World;
using Fetih.Web.Audit;
using Fetih.Web.Features.Catalogs;

public sealed class DataEditService
{
    private readonly WebConfiguration Config;
    private readonly CatalogStore Catalogs;
    private readonly ClientFiles Client;
    private readonly SemaphoreSlim WriteGate = new(1, 1);

    public DataEditService(WebConfiguration config, CatalogStore catalogs, ClientFiles clientFiles)
    {
        this.Config = config;
        this.Catalogs = catalogs;
        this.Client = clientFiles;
    }

    internal DataEditService(WebConfiguration config, CatalogStore catalogs)
        : this(config, catalogs, new ClientFiles(config))
    {
    }

    public LevelsFileResponse ReadLevels()
    {
        var dataDir = this.ResolveDataDirectory();
        var set = this.CreateDataSet(dataDir);
        var file = (DataFile<LevelExp>)set.File(DataFileKinds.Levels.FileName);
        var entries = new LevelEntryDto[file.Count];
        for (int i = 0; i < file.Count; i++)
        {
            var e = file[i];
            entries[i] = new LevelEntryDto(e.Level, e.Experience, e.UpLevTime, file.LineOf(i));
        }
        return new LevelsFileResponse(file.Hash, entries, ToDtos(set.Validate(DataFileKinds.Levels.FileName)));
    }

    public Task<SaveDataResponse> SaveLevelsAsync(SaveLevelsRequest request, AuditLog audit, CancellationToken ct) =>
        this.SaveAsync(
            DataFileKinds.Levels,
            request.Hash,
            request.Entries.Select(e => new LevelExp
            {
                Level = e.Level,
                Experience = e.Experience,
                UpLevTime = e.UpLevTime,
            }).ToList(),
            e => e.Level,
            (a, b) => a.Level == b.Level && a.Experience == b.Experience && a.UpLevTime == b.UpLevTime,
            audit,
            ct);

    public GoalsFileResponse ReadGoals()
    {
        var dataDir = this.ResolveDataDirectory();
        var set = this.CreateDataSet(dataDir);
        var file = (DataFile<Npc>)set.File(DataFileKinds.Goals.FileName);
        var entries = new GoalEntryDto[file.Count];
        for (int i = 0; i < file.Count; i++)
        {
            var e = file[i];
            entries[i] = new GoalEntryDto(e.NpcID, e.Lookface, e.Role, e.Level, e.Life, e.MapID, e.X, e.Y, file.LineOf(i));
        }
        return new GoalsFileResponse(file.Hash, entries, ToDtos(set.Validate(DataFileKinds.Goals.FileName)));
    }

    public Task<SaveDataResponse> SaveGoalsAsync(SaveGoalsRequest request, AuditLog audit, CancellationToken ct) =>
        this.SaveAsync(
            DataFileKinds.Goals,
            request.Hash,
            request.Entries.Select(e => new Npc
            {
                NpcID = e.Id,
                Lookface = e.Lookface,
                Role = e.Role,
                Level = e.Level,
                Life = e.Life,
                MapID = e.Map,
                X = e.X,
                Y = e.Y,
            }).ToList(),
            e => e.NpcID,
            Npc.Same,
            audit,
            ct);

    public TeleportersFileResponse ReadTeleporters()
    {
        var dataDir = this.ResolveDataDirectory();
        var set = this.CreateDataSet(dataDir);
        var npcs = ReadNpcLocations(set, dataDir);
        var file = (DataFile<Teleporter>)set.File(DataFileKinds.Teleporters.FileName);
        var entries = new TeleporterEntryDto[file.Count];
        for (int i = 0; i < file.Count; i++)
        {
            var e = file[i];
            npcs.TryGetValue(e.NpcID, out var pos);
            entries[i] = new TeleporterEntryDto(
                e.NpcID,
                e.Fare,
                e.Face,
                e.Text?.ToArray() ?? [],
                e.Destinations?.Select(d => new TeleporterDestinationDto(d.Name ?? "", d.MapID, d.X, d.Y)).ToArray() ?? [],
                e.Close,
                file.LineOf(i),
                pos.HasPos ? pos.Map : null,
                pos.HasPos ? pos.X : null,
                pos.HasPos ? pos.Y : null);
        }
        return new TeleportersFileResponse(file.Hash, entries, ToDtos(set.Validate(DataFileKinds.Teleporters.FileName)));
    }

    public Task<SaveDataResponse> SaveTeleportersAsync(SaveTeleportersRequest request, AuditLog audit, CancellationToken ct) =>
        this.SaveAsync(
            DataFileKinds.Teleporters,
            request.Hash,
            request.Entries.Select(e => new Teleporter
            {
                NpcID = e.Id,
                Fare = e.Fare,
                Face = e.Face,
                Text = e.Text?.ToList() ?? [],
                Destinations = e.Destinations?.Select(d => new Destination
                {
                    Name = d.Name,
                    MapID = d.Map,
                    X = d.X,
                    Y = d.Y,
                }).ToList() ?? [],
                Close = string.IsNullOrWhiteSpace(e.Close) ? null : e.Close,
            }).ToList(),
            e => e.NpcID,
            TeleporterSame,
            audit,
            ct);

    public TalkersFileResponse ReadTalkers()
    {
        var dataDir = this.ResolveDataDirectory();
        var set = this.CreateDataSet(dataDir);
        var npcs = ReadNpcLocations(set, dataDir);
        var file = (DataFile<Talker>)set.File(DataFileKinds.Talkers.FileName);
        var entries = new TalkerEntryDto[file.Count];
        for (int i = 0; i < file.Count; i++)
        {
            var e = file[i];
            npcs.TryGetValue(e.NpcID, out var pos);
            entries[i] = new TalkerEntryDto(
                e.NpcID,
                e.Face,
                e.Text?.ToArray() ?? [],
                e.Reply ?? "",
                file.LineOf(i),
                pos.HasPos ? pos.Map : null,
                pos.HasPos ? pos.X : null,
                pos.HasPos ? pos.Y : null);
        }
        return new TalkersFileResponse(file.Hash, entries, ToDtos(set.Validate(DataFileKinds.Talkers.FileName)));
    }

    public Task<SaveDataResponse> SaveTalkersAsync(SaveTalkersRequest request, AuditLog audit, CancellationToken ct) =>
        this.SaveAsync(
            DataFileKinds.Talkers,
            request.Hash,
            request.Entries.Select(e => new Talker
            {
                NpcID = e.Id,
                Face = e.Face,
                Text = e.Text?.ToList() ?? [],
                Reply = e.Reply ?? "",
            }).ToList(),
            e => e.NpcID,
            TalkerSame,
            audit,
            ct);

    public MapPickInfoResponse GetMapPickInfo(uint mapId)
    {
        this.Catalogs.MapsById.TryGetValue(mapId, out var cat);
        var grid = this.Client.TryGetGrid(mapId);
        int? width = grid?.Width;
        int? height = grid?.Height;

        string? background = null;
        var assetsDir = WebConfiguration.PathsSection.Resolve(this.Config.Paths.AssetsDirectory);
        if (assetsDir != null)
        {
            if (File.Exists(Path.Combine(assetsDir, "maps", mapId.ToString(), "bg.png")))
                background = $"/assets/maps/{mapId}/bg.png";
            else if (File.Exists(Path.Combine(assetsDir, "maps", $"{mapId}.png")))
                background = $"/assets/maps/{mapId}.png";
        }

        string? name = this.Catalogs.MapDisplayName(mapId);
        return new MapPickInfoResponse(mapId, name, width, height, cat?.Minimap, background);
    }

    public CellCheckResponse CheckCell(uint mapId, int x, int y)
    {
        var grid = this.Client.TryGetGrid(mapId);
        if (grid == null)
            return new CellCheckResponse(mapId, x, y, false, false, false);

        bool inBounds = grid.Contains(x, y);
        bool walkable = inBounds && grid.IsWalkable(x, y);
        return new CellCheckResponse(mapId, x, y, true, inBounds, walkable);
    }

    private async Task<SaveDataResponse> SaveAsync<T, TKey>(
        DataFileKind kind,
        string expectedHash,
        IReadOnlyList<T> next,
        Func<T, TKey> keyOf,
        Func<T, T, bool> same,
        AuditLog audit,
        CancellationToken ct)
        where T : class
        where TKey : notnull
    {
        await this.WriteGate.WaitAsync(ct);
        try
        {
            var dataDir = this.ResolveDataDirectory();
            var path = Path.Combine(dataDir, kind.FileName);
            var file = DataFile<T>.Load(path, kind.ArrayKey);
            file.EnsureHash(expectedHash);

            var (added, updated, removed) = ApplyEntries(file, next, keyOf, same);

            var set = this.CreateDataSet(dataDir);
            set.Use(kind.FileName, file);
            var problems = set.Validate(kind.FileName);
            bool hasErrors = problems.Any(p => p.Level == DataProblemLevel.Error);
            if (hasErrors)
            {
                return new SaveDataResponse(file.Hash, false, ToDtos(problems), added, updated, removed);
            }

            if (file.IsChanged)
            {
                file.Save();
                await audit.WriteAsync(
                    "data.save",
                    "data_file",
                    kind.FileName,
                    after: new { added, updated, removed, count = file.Count },
                    summary: $"Saved {kind.FileName}: +{added} ~{updated} -{removed}");
            }

            return new SaveDataResponse(file.Hash, true, ToDtos(problems), added, updated, removed);
        }
        finally
        {
            this.WriteGate.Release();
        }
    }

    // Patches entries in place by key so unchanged entries keep their exact formatting and comments.
    public static (int Added, int Updated, int Removed) ApplyEntries<T, TKey>(
        DataFile<T> file,
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
        var existingMatches = new int[file.Count];
        for (int i = 0; i < file.Count; i++)
        {
            var k = keyOf(file[i]);
            if (nextByKey.TryGetValue(k, out var q) && q.Count > 0)
            {
                int nextIdx = q.Dequeue();
                existingMatches[i] = nextIdx;
                matchedNext[nextIdx] = true;
            }
            else
            {
                existingMatches[i] = -1;
            }
        }

        int updated = 0;
        int removed = 0;
        for (int i = file.Count - 1; i >= 0; i--)
        {
            int nextIdx = existingMatches[i];
            if (nextIdx < 0)
            {
                file.RemoveAt(i);
                removed++;
            }
            else
            {
                var current = file[i];
                var target = next[nextIdx];
                if (!same(current, target))
                {
                    file[i] = target;
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

    private static bool TeleporterSame(Teleporter a, Teleporter b)
    {
        if (a.NpcID != b.NpcID || a.Fare != b.Fare || a.Face != b.Face || a.Close != b.Close)
            return false;
        var ta = a.Text ?? [];
        var tb = b.Text ?? [];
        if (ta.Count != tb.Count || !ta.SequenceEqual(tb))
            return false;
        var da = a.Destinations ?? [];
        var db = b.Destinations ?? [];
        if (da.Count != db.Count)
            return false;
        for (int i = 0; i < da.Count; i++)
            if (da[i] != db[i])
                return false;
        return true;
    }

    private static bool TalkerSame(Talker a, Talker b)
    {
        if (a.NpcID != b.NpcID || a.Face != b.Face || a.Reply != b.Reply)
            return false;
        var ta = a.Text ?? [];
        var tb = b.Text ?? [];
        return ta.Count == tb.Count && ta.SequenceEqual(tb);
    }

    private static Dictionary<uint, (bool HasPos, uint Map, ushort X, ushort Y)> ReadNpcLocations(DataSet set, string dataDir)
    {
        var map = new Dictionary<uint, (bool, uint, ushort, ushort)>();
        var npcsPath = Path.Combine(dataDir, DataFileKinds.Npcs.FileName);
        if (!File.Exists(npcsPath))
            return map;
        try
        {
            var npcs = (DataFile<Npc>)set.File(DataFileKinds.Npcs.FileName);
            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                map.TryAdd(n.NpcID, (true, n.MapID, n.X, n.Y));
            }
        }
        catch
        {
            // Ignore if npcs.json is unreadable
        }
        return map;
    }

    private string ResolveDataDirectory() => StudioData.ResolveDataDirectory(this.Config);

    private DataSet CreateDataSet(string dataDir) => this.Client.CreateDataSet(dataDir);

    private static DataProblemDto[] ToDtos(IReadOnlyList<DataProblem> problems) =>
        problems.Select(p => new DataProblemDto(
            p.File,
            p.Entry,
            p.Line,
            p.Message,
            p.Level == DataProblemLevel.Warning ? "warning" : "error")).ToArray();
}

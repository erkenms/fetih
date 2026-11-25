namespace Comet.Web.Features.MonsterEdit;

using Comet.Game.Combat;
using Comet.Game.DataEditing;
using Comet.Game.World;
using Comet.Web.Features.Catalogs;

public sealed class MonsterDataService(WebConfiguration config, CatalogStore catalogs, ClientFiles clientFiles)
{
    private readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string? DataDirectoryOverride;

    internal MonsterDataService(string dataDirectoryOverride, WebConfiguration config, CatalogStore catalogs)
        : this(config, catalogs, new ClientFiles(config))
    {
        this.DataDirectoryOverride = dataDirectoryOverride;
    }

    public string ResolveDataDirectory() =>
        !string.IsNullOrEmpty(this.DataDirectoryOverride) ? this.DataDirectoryOverride : StudioData.ResolveDataDirectory(config);

    public DataSet CreateDataSet(string dataDir) => clientFiles.CreateDataSet(dataDir);

    public DataFile<MonsterType> LoadMonstersFile(string dataDir) =>
        (DataFile<MonsterType>)DataFileKinds.Monsters.Load(Path.Combine(dataDir, DataFileKinds.Monsters.FileName));

    public async Task<T> WithLockAsync<T>(Func<string, Task<T>> action, CancellationToken ct)
    {
        await this.Gate.WaitAsync(ct);
        try
        {
            return await action(this.ResolveDataDirectory());
        }
        finally
        {
            this.Gate.Release();
        }
    }

    public IReadOnlyList<Generator> LoadSpawns(string dataDir)
    {
        string path = Path.Combine(dataDir, DataFileKinds.Spawns.FileName);
        if (!File.Exists(path))
            return [];
        try
        {
            return ((DataFile<Generator>)DataFileKinds.Spawns.Load(path)).All();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<BossSpawn> LoadBosses(string dataDir)
    {
        string path = Path.Combine(dataDir, DataFileKinds.Bosses.FileName);
        if (!File.Exists(path))
            return [];
        try
        {
            return ((DataFile<BossSpawn>)DataFileKinds.Bosses.Load(path)).All();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<MonsterDrops> LoadDrops(string dataDir)
    {
        string path = Path.Combine(dataDir, DataFileKinds.Drops.FileName);
        if (!File.Exists(path))
            return [];
        try
        {
            return ((DataFile<MonsterDrops>)DataFileKinds.Drops.Load(path)).All();
        }
        catch
        {
            return [];
        }
    }

    public string? MapName(uint mapId)
    {
        if (!catalogs.MapsById.TryGetValue(mapId, out var map))
            return null;
        return !string.IsNullOrWhiteSpace(map.ServerName) ? map.ServerName : map.Name;
    }

    public MonsterProblemDto[] ValidateMonsters(
        string dataDir,
        DataFile<MonsterType> monstersFile,
        bool checkDependents)
    {
        var dataSet = this.CreateDataSet(dataDir);
        dataSet.Use(DataFileKinds.Monsters.FileName, monstersFile);

        var problems = new List<DataProblem>(dataSet.Validate(DataFileKinds.Monsters.FileName));
        if (checkDependents)
        {
            foreach (var kind in new[] { DataFileKinds.Spawns, DataFileKinds.Bosses, DataFileKinds.Drops })
            {
                if (File.Exists(Path.Combine(dataDir, kind.FileName)))
                    problems.AddRange(dataSet.Validate(kind.FileName));
            }
        }

        return problems.Select(ToDto).ToArray();
    }

    public static MonsterDto ToDto(MonsterType m) => new(
        Id: m.ID,
        Name: m.Name ?? "",
        Lookface: m.Lookface,
        Level: m.Level,
        Life: m.Life,
        AttackMin: m.AttackMin,
        AttackMax: m.AttackMax,
        Defense: m.Defense,
        Dexterity: m.Dexterity,
        Dodge: m.Dodge,
        AttackRange: m.AttackRange,
        ViewRange: m.ViewRange,
        AttackSpeed: m.AttackSpeed,
        MoveSpeed: m.MoveSpeed,
        AttackUser: m.AttackUser,
        DropMoney: m.DropMoney,
        DropHp: m.DropHp,
        DropMp: m.DropMp,
        Boss: m.Boss,
        MagicType: m.MagicType,
        MagicHitRate: m.MagicHitRate,
        Spells: m.Spells ?? []);

    public static MonsterType FromDto(MonsterDto d) => new()
    {
        ID = d.Id,
        Name = d.Name.Trim(),
        Lookface = d.Lookface,
        Level = d.Level,
        Life = d.Life,
        AttackMin = d.AttackMin,
        AttackMax = d.AttackMax,
        Defense = d.Defense,
        Dexterity = d.Dexterity,
        Dodge = d.Dodge,
        AttackRange = d.AttackRange,
        ViewRange = d.ViewRange,
        AttackSpeed = d.AttackSpeed,
        MoveSpeed = d.MoveSpeed,
        AttackUser = d.AttackUser,
        DropMoney = d.DropMoney,
        DropHp = d.DropHp,
        DropMp = d.DropMp,
        Boss = d.Boss,
        MagicType = d.MagicType,
        MagicHitRate = d.MagicHitRate,
        // Omit empty spells array so JSON diffs stay minimal
        Spells = d.Spells is { Length: > 0 } ? d.Spells : null,
    };

    public static MonsterProblemDto ToDto(DataProblem p) =>
        new(p.File, p.Entry, p.Line, p.Message, p.Level == DataProblemLevel.Warning ? "warning" : "error");
}

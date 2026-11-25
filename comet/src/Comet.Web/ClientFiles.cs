namespace Comet.Web;

using System.Text;
using Comet.Game.DataEditing;
using Comet.Game.Items;
using Comet.Game.World;

// Shared, lazily loaded client files (GameMap.dat / DMaps, itemtype.dat, ini/npc.ini) for Web services
public sealed class ClientFiles
{
    private readonly Lazy<MapStore?> Grids;
    private readonly Lazy<ItemTypeTable?> Items;
    private readonly Lazy<IReadOnlyDictionary<int, string>> NpcNamesCache;

    public string? ClientDir { get; }

    public ClientFiles(WebConfiguration config)
        : this(WebConfiguration.PathsSection.Resolve(config.Paths.ClientDirectory))
    {
    }

    internal ClientFiles(string? clientDir)
    {
        this.ClientDir = clientDir;
        this.Grids = new Lazy<MapStore?>(this.LoadMapStore, LazyThreadSafetyMode.ExecutionAndPublication);
        this.Items = new Lazy<ItemTypeTable?>(this.LoadItemTypes, LazyThreadSafetyMode.ExecutionAndPublication);
        this.NpcNamesCache = new Lazy<IReadOnlyDictionary<int, string>>(this.LoadNpcNames, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyDictionary<int, string> NpcNames => this.NpcNamesCache.Value;

    public MapGrid? TryGetGrid(uint mapId)
    {
        var store = this.Grids.Value;
        if (store == null || !store.Exists(mapId))
            return null;
        try
        {
            return store.Get(mapId);
        }
        catch
        {
            return null;
        }
    }

    // Checks against the cached client maps and items when available, else the files alone
    public DataSet CreateDataSet(string dataDir)
    {
        var store = this.Grids.Value;
        var items = this.Items.Value;
        return new DataSet(
            dataDir,
            store != null ? id => store.Exists(id) ? store.Get(id) : null : null,
            items != null ? id => items.TryGet(id, out _) : null);
    }

    private MapStore? LoadMapStore()
    {
        if (string.IsNullOrEmpty(this.ClientDir) || !Directory.Exists(this.ClientDir))
            return null;
        try { return new MapStore(this.ClientDir); }
        catch { return null; }
    }

    private ItemTypeTable? LoadItemTypes()
    {
        if (string.IsNullOrEmpty(this.ClientDir) || !Directory.Exists(this.ClientDir))
            return null;
        try { return ItemTypeTable.Load(this.ClientDir); }
        catch { return null; }
    }

    // Reads ini/npc.ini: [NpcType<n>] -> Name=<name>
    private IReadOnlyDictionary<int, string> LoadNpcNames()
    {
        var result = new Dictionary<int, string>();
        var iniPath = MapStore.ResolveFile(this.ClientDir, "ini/npc.ini");
        if (iniPath == null)
            return result;

        try
        {
            int currentType = -1;
            foreach (var raw in File.ReadLines(iniPath, Encoding.Latin1))
            {
                var line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var section = line[1..^1];
                    currentType = section.StartsWith("NpcType", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(section[7..], out var id)
                        ? id : -1;
                }
                else if (currentType >= 0 && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    var name = line[5..].Trim();
                    if (name.Length > 0)
                        result.TryAdd(currentType, name);
                }
            }
        }
        catch
        {
            // Ignore unreadable npc.ini
        }

        return result;
    }
}

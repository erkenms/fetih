namespace Fetih.Web.Features.Looks;

using System.Text.Json;
using Fetih.Web.Features.Catalogs;
using Fetih.Game.World;

// The NPC and monster looks the asset build rendered (looks.json): where each picture is and
// the pixel in it the role stands on
public class LooksFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<LookStore>();
    }
}

public sealed record NpcLook(uint Lookface, string Image, int X, int Y, string? Name);

// ZoomPercent: how much larger than its look the client draws the monster (monster.dat)
public sealed record LookMonster(uint Id, string Name, int ZoomPercent);

public sealed record MonsterLook(uint Look, string Image, int X, int Y, LookMonster[] Monsters);

public sealed record LooksCatalogResponse(int PreviewDirection, NpcLook[] Npc, MonsterLook[] Monster);

public sealed class LookStore
{
    private readonly Lazy<LooksCatalogResponse> looks;

    public LookStore(WebConfiguration config, ClientFiles client, CatalogStore catalogs)
        : this(WebConfiguration.PathsSection.Resolve(config.Paths.AssetsDirectory),
            () => client.NpcNames, () => catalogs.Monsters)
    {
    }

    internal LookStore(string? assetsDir, Func<IReadOnlyDictionary<int, string>> npcNames,
        Func<IReadOnlyList<MonsterCatalogEntry>> monsters)
    {
        this.looks = new Lazy<LooksCatalogResponse>(() => Load(assetsDir, npcNames(), monsters()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public LooksCatalogResponse Looks => this.looks.Value;

    private static LooksCatalogResponse Load(string? assetsDir, IReadOnlyDictionary<int, string> npcNames,
        IReadOnlyList<MonsterCatalogEntry> monsters)
    {
        var path = string.IsNullOrEmpty(assetsDir) ? null : MapStore.ResolveFile(assetsDir, "looks.json");
        AssetJson? raw = null;
        try
        {
            if (path != null)
                raw = JsonSerializer.Deserialize<AssetJson>(File.ReadAllText(path), JsonSerializerOptions.Web);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
        }
        if (raw == null)
            return new LooksCatalogResponse(0, [], []);

        var byLook = monsters.Where(m => m.Lookface != null && m.InServer)
            .GroupBy(m => m.Lookface!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Id).Select(m => new LookMonster(m.Id, m.Name, m.ZoomPercent ?? 100)).ToArray());
        return new LooksCatalogResponse(
            raw.PreviewDirection,
            (raw.Npc ?? []).OrderBy(e => e.Key)
                .Select(e => new NpcLook(e.Key, e.Value.Image, e.Value.X, e.Value.Y,
                    npcNames.GetValueOrDefault((int)(e.Key / 10))))
                .ToArray(),
            (raw.Monster ?? []).OrderBy(e => e.Key)
                .Select(e => new MonsterLook(e.Key, e.Value.Image, e.Value.X, e.Value.Y, byLook.GetValueOrDefault(e.Key) ?? []))
                .ToArray());
    }

    private sealed record Sprite(string Image, int X, int Y);

    private sealed record AssetJson(int PreviewDirection, Dictionary<uint, Sprite>? Npc, Dictionary<uint, Sprite>? Monster);
}

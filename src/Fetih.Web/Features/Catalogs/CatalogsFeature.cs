namespace Fetih.Web.Features.Catalogs;

using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public class CatalogsFeature : IFeature
{
    public const int MaxPageSize = 200;

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<CatalogStore>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/catalogs").RequirePermission(Permission.ViewCatalogs);

        group.MapGet("/monsters", ListMonsters).WithName("listCatalogMonsters");
        group.MapGet("/monsters/{id:long}", GetMonster).WithName("getCatalogMonster");

        group.MapGet("/spells", ListSpells).WithName("listCatalogSpells");
        group.MapGet("/spells/{type:int}", GetSpell).WithName("getCatalogSpell");

        group.MapGet("/maps", ListMaps).WithName("listCatalogMaps");
        group.MapGet("/maps/{id:long}", GetMap).WithName("getCatalogMap");

        group.MapGet("/faces", ListFaces).WithName("listCatalogFaces");
        group.MapGet("/shops", ListShops).WithName("listCatalogShops");
    }

    private static Ok<MonsterCatalogPage> ListMonsters(
        CatalogStore store,
        string? search,
        bool? boss,
        int? minLevel,
        int? maxLevel,
        string? source,
        int page = 1,
        int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        IEnumerable<MonsterCatalogEntry> query = store.Monsters;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            bool isId = uint.TryParse(term, out var id);
            query = query.Where(m =>
                (isId && m.Id == id) ||
                m.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (boss != null)
            query = query.Where(m => m.Boss == boss.Value);
        if (minLevel != null)
            query = query.Where(m => (m.Level ?? m.ClientLevel ?? 0) >= minLevel.Value);
        if (maxLevel != null)
            query = query.Where(m => (m.Level ?? m.ClientLevel ?? 0) <= maxLevel.Value);
        if (source is "server")
            query = query.Where(m => m.InServer);
        else if (source is "client")
            query = query.Where(m => m.InClient);

        var filtered = query.ToArray();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return TypedResults.Ok(new MonsterCatalogPage(items, filtered.Length));
    }

    private static Results<Ok<MonsterCatalogEntry>, NotFound<ApiError>> GetMonster(CatalogStore store, long id) =>
        id is >= 0 and <= uint.MaxValue && store.MonstersById.TryGetValue((uint)id, out var monster)
            ? TypedResults.Ok(monster)
            : TypedResults.NotFound(new ApiError("not_found"));

    private static Ok<SpellCatalogPage> ListSpells(
        CatalogStore store,
        string? search,
        string? sort,
        bool? xpOnly,
        bool? hasIcon,
        int page = 1,
        int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        IEnumerable<SpellCatalogEntry> query = store.Spells;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            bool isId = ushort.TryParse(term, out var type);
            query = query.Where(s =>
                (isId && s.Type == type) ||
                s.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(sort))
            query = query.Where(s => s.Sort.Equals(sort.Trim(), StringComparison.OrdinalIgnoreCase));
        if (xpOnly == true)
            query = query.Where(s => s.UseXp == 1);
        if (hasIcon == true)
            query = query.Where(s => !string.IsNullOrEmpty(s.Icon));

        var filtered = query.ToArray();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return TypedResults.Ok(new SpellCatalogPage(items, filtered.Length, [.. store.SpellSorts]));
    }

    private static Results<Ok<SpellCatalogEntry>, NotFound<ApiError>> GetSpell(CatalogStore store, int type) =>
        type is >= 0 and <= ushort.MaxValue && store.SpellsByType.TryGetValue((ushort)type, out var spell)
            ? TypedResults.Ok(spell)
            : TypedResults.NotFound(new ApiError("not_found"));

    private static Ok<MapCatalogPage> ListMaps(
        CatalogStore store,
        string? search,
        bool? hasMinimap,
        int page = 1,
        int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        IEnumerable<MapCatalogEntry> query = store.Maps;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            bool isId = uint.TryParse(term, out var id);
            query = query.Where(m =>
                (isId && m.Id == id) ||
                (m.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.ServerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                m.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        if (hasMinimap == true)
            query = query.Where(m => !string.IsNullOrEmpty(m.Minimap));

        var filtered = query.ToArray();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return TypedResults.Ok(new MapCatalogPage(items, filtered.Length));
    }

    private static Results<Ok<MapCatalogEntry>, NotFound<ApiError>> GetMap(CatalogStore store, long id) =>
        id is >= 0 and <= uint.MaxValue && store.MapsById.TryGetValue((uint)id, out var map)
            ? TypedResults.Ok(map)
            : TypedResults.NotFound(new ApiError("not_found"));

    private static Ok<FacesCatalogResponse> ListFaces(CatalogStore store) =>
        TypedResults.Ok(store.Faces);

    private static Ok<ShopCatalogResponse> ListShops(CatalogStore store, string? search, string? source)
    {
        IEnumerable<ShopCatalogEntry> query = store.Shops;
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(s => s.Source.Equals(source.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            bool isId = uint.TryParse(term, out var id);
            query = query.Select(s =>
            {
                bool shopMatches =
                    (isId && s.NpcId == id) ||
                    s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    s.Source.Contains(term, StringComparison.OrdinalIgnoreCase);
                if (shopMatches)
                    return s;

                var matchingItems = s.Items.Where(i =>
                    (isId && i.ItemId == id) ||
                    (i.ItemName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (i.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();

                return matchingItems.Length > 0
                    ? s with { Items = matchingItems, ItemCount = matchingItems.Length }
                    : null;
            }).OfType<ShopCatalogEntry>();
        }

        return TypedResults.Ok(new ShopCatalogResponse(query.ToArray(), [.. store.ShopSources]));
    }
}

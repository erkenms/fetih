namespace Fetih.Web.Features.Maps;

using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public class MapsFeature : IFeature
{
    public const int MaxPageSize = 200;

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MapDataStore>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/maps").RequirePermission(Permission.ViewCatalogs);

        group.MapGet("/", ListMaps).WithName("listMaps");
        group.MapGet("/{id:long}", GetMapDetail).WithName("getMapDetail");
        group.MapGet("/{id:long}/grid", GetMapGrid).WithName("getMapGrid");
    }

    private static Ok<MapListPage> ListMaps(
        MapDataStore store,
        string? search,
        bool? hasMinimap,
        bool? hasTiles,
        bool? hasEntities,
        int page = 1,
        int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        IEnumerable<MapSummary> query = store.ListMaps();
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
        if (hasTiles == true)
            query = query.Where(m => m.HasTiles);
        if (hasEntities == true)
            query = query.Where(m => m.NpcCount > 0 || m.SpawnCount > 0 || m.PortalCount > 0);

        var filtered = query.ToArray();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return TypedResults.Ok(new MapListPage(items, filtered.Length));
    }

    private static Results<Ok<MapDetail>, NotFound<ApiError>> GetMapDetail(MapDataStore store, long id) =>
        id is >= 0 and <= uint.MaxValue && store.GetMapDetail((uint)id) is { } detail
            ? TypedResults.Ok(detail)
            : TypedResults.NotFound(new ApiError("not_found"));

    private static Results<Ok<MapGridResponse>, NotFound<ApiError>> GetMapGrid(MapDataStore store, long id) =>
        id is >= 0 and <= uint.MaxValue && store.GetMapGrid((uint)id) is { } grid
            ? TypedResults.Ok(grid)
            : TypedResults.NotFound(new ApiError("not_found"));
}

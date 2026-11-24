namespace Comet.Web.Features.Items;

using System.Globalization;
using Comet.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public record ItemSearchPage(CatalogItem[] Items, int Total, bool AssetsAvailable);

public class ItemsFeature : IFeature
{
    public const int MaxPageSize = 200;
    public const int MaxBatchSize = 200;

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<ItemCatalog>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/items").RequirePermission(Permission.ViewCatalogs);
        group.MapGet("/", SearchItems).WithName("searchItems");
        group.MapGet("/batch", GetItemsByIds).WithName("getItemsByIds");
        group.MapGet("/{id:long}", GetItem).WithName("getItem");
    }

    private static Ok<ItemSearchPage> SearchItems(
        ItemCatalog catalog,
        string? q,
        ItemCategory? category,
        ItemQuality? quality,
        byte? profession,
        byte? minLevel,
        byte? maxLevel,
        string? type,
        int page = 1,
        int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total, assetsAvailable) = catalog.Search(
            q, category, quality, profession, minLevel, maxLevel, type, page, pageSize);

        return TypedResults.Ok(new ItemSearchPage(items, total, assetsAvailable));
    }

    private static Results<Ok<CatalogItem>, NotFound<ApiError>> GetItem(ItemCatalog catalog, long id)
    {
        if (id is < 0 or > uint.MaxValue)
            return TypedResults.NotFound(new ApiError("not_found"));

        return catalog.TryGet((uint)id, out var item) && item != null
            ? TypedResults.Ok(item)
            : TypedResults.NotFound(new ApiError("not_found"));
    }

    private static Ok<CatalogItem[]> GetItemsByIds(ItemCatalog catalog, string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return TypedResults.Ok(Array.Empty<CatalogItem>());

        var parsed = new List<uint>();
        foreach (string part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out uint id))
            {
                parsed.Add(id);
                if (parsed.Count >= MaxBatchSize)
                    break;
            }
        }

        return TypedResults.Ok(catalog.GetMany(parsed));
    }
}

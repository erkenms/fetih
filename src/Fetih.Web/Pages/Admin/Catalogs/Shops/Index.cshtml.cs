namespace Fetih.Web.Pages.Admin.Catalogs.Shops;

using Fetih.Web.Auth;
using Fetih.Web.Features.Catalogs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class IndexModel(CatalogStore store) : PageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "source")]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true, Name = "sayfa")]
    public int PageNo { get; set; } = 1;

    public string[] Sources => [.. store.ShopSources];

    public ShopCatalogEntry[] Shops { get; private set; } = [];

    public int Total { get; private set; }

    public int Pages => Math.Max(1, (this.Total + PageSize - 1) / PageSize);

    public void OnGet()
    {
        IEnumerable<ShopCatalogEntry> query = store.Shops;
        if (!string.IsNullOrWhiteSpace(this.Source))
            query = query.Where(s => s.Source.Equals(this.Source.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(this.Query))
        {
            string term = this.Query.Trim();
            bool isId = uint.TryParse(term, out uint id);
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
        var all = query.ToArray();
        this.Total = all.Length;
        this.PageNo = Math.Clamp(this.PageNo, 1, this.Pages);
        this.Shops = [.. all.Skip((this.PageNo - 1) * PageSize).Take(PageSize)];
    }
}

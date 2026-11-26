namespace Fetih.Web.Pages.Admin.Catalogs.Spells;

using Fetih.Web.Auth;
using Fetih.Web.Features.Catalogs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class IndexModel(CatalogStore store) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "sort")]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "xpOnly")]
    public bool XpOnly { get; set; }

    [BindProperty(SupportsGet = true, Name = "sayfa")]
    public int PageNo { get; set; } = 1;

    public string[] Sorts => [.. store.SpellSorts];

    public SpellCatalogEntry[] Spells { get; private set; } = [];

    public int Total { get; private set; }

    public int Pages => Math.Max(1, (this.Total + PageSize - 1) / PageSize);

    public void OnGet()
    {
        IEnumerable<SpellCatalogEntry> query = store.Spells;
        if (!string.IsNullOrWhiteSpace(this.Query))
        {
            string term = this.Query.Trim();
            bool isType = ushort.TryParse(term, out ushort type);
            query = query.Where(s => (isType && s.Type == type) || s.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(this.Sort))
            query = query.Where(s => s.Sort.Equals(this.Sort.Trim(), StringComparison.OrdinalIgnoreCase));
        if (this.XpOnly)
            query = query.Where(s => s.UseXp == 1);
        var all = query.OrderBy(s => s.Sort).ThenBy(s => s.Levels.Length > 0 ? s.Levels.Min(l => l.NeedLevel) : 0)
            .ThenBy(s => s.Type).ToArray();
        this.Total = all.Length;
        this.PageNo = Math.Clamp(this.PageNo, 1, this.Pages);
        this.Spells = [.. all.Skip((this.PageNo - 1) * PageSize).Take(PageSize)];
    }
}

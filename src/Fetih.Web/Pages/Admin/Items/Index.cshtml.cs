namespace Fetih.Web.Pages.Admin.Items;

using Fetih.Web.Auth;
using Fetih.Web.Features.Items;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class IndexModel(ItemCatalog catalog) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "category")]
    public ItemCategory? Category { get; set; }

    [BindProperty(SupportsGet = true, Name = "minLevel")]
    public byte? MinLevel { get; set; }

    [BindProperty(SupportsGet = true, Name = "maxLevel")]
    public byte? MaxLevel { get; set; }

    [BindProperty(SupportsGet = true, Name = "sayfa")]
    public int PageNo { get; set; } = 1;

    public CatalogItem[] Items { get; private set; } = [];

    public int Total { get; private set; }

    public bool AssetsAvailable { get; private set; }

    public int Pages => Math.Max(1, (this.Total + PageSize - 1) / PageSize);

    public void OnGet()
    {
        this.PageNo = Math.Max(1, this.PageNo);
        (this.Items, this.Total, this.AssetsAvailable) = catalog.Search(
            this.Query, this.Category, null, null, this.MinLevel, this.MaxLevel, null, this.PageNo, PageSize);
        if (this.PageNo > this.Pages)
        {
            this.PageNo = this.Pages;
            (this.Items, this.Total, this.AssetsAvailable) = catalog.Search(
                this.Query, this.Category, null, null, this.MinLevel, this.MaxLevel, null, this.PageNo, PageSize);
        }
    }
}

namespace Fetih.Web.Pages;

using Fetih.Web.Features.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class HaberlerModel(PublicSiteService site) : PageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public int Sayfa { get; set; } = 1;

    public NewsPage News { get; private set; } = new([], 0);

    public int Pages => Math.Max(1, (int)Math.Ceiling(this.News.Total / (double)PageSize));

    public async Task OnGetAsync(CancellationToken ct)
    {
        this.Sayfa = Math.Max(1, this.Sayfa);
        this.News = await site.GetNewsAsync(this.Sayfa, PageSize, ct);
    }
}

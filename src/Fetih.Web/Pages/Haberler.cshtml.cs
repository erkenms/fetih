namespace Fetih.Web.Pages;

using System.Globalization;
using System.Text;
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

    // /haberler?handler=launcher: the launcher's parchment, one "date<TAB>subject" line each
    public async Task<IActionResult> OnGetLauncherAsync(CancellationToken ct)
    {
        var news = await site.GetNewsAsync(1, 10, ct);
        var text = new StringBuilder();
        foreach (var item in news.Items)
            text.Append(WebTime.ToLocal(item.Date).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
                .Append('\t').Append(item.Subject.Replace('\t', ' ')).Append('\n');
        return this.Content(text.ToString(), "text/plain; charset=utf-8");
    }
}

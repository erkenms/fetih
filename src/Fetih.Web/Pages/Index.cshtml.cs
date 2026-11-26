namespace Fetih.Web.Pages;

using Fetih.Web.Features.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class IndexModel(PublicSiteService site) : PageModel
{
    public PublicHomeResponse Home { get; private set; } = null!;

    public string BestTab { get; private set; } = "seviye";

    public async Task OnGetAsync(string? eniyi, CancellationToken ct)
    {
        this.BestTab = eniyi switch
        {
            "soyluluk" or "lonca" or "arena" => eniyi,
            _ => "seviye",
        };
        this.Home = await site.GetHomeAsync(ct);
    }
}

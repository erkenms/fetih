namespace Fetih.Web.Pages;

using Fetih.Web.Features.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class IndirModel(PublicSiteService site) : PageModel
{
    public DownloadInfoResponse Info { get; private set; } = new(null, "5619");

    public void OnGet() => this.Info = site.GetDownloadInfo();
}

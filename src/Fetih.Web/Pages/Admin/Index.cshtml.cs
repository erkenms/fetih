namespace Fetih.Web.Pages.Admin;

using Fetih.Web.Auth;
using Fetih.Web.Features.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.AdminArea)]
public class IndexModel(ServerControlService server) : PageModel
{
    public ServerControlResponse Status { get; private set; } =
        new(false, false, "", "", false, null, null, "", 0, 0, null);

    public async Task OnGetAsync() => this.Status = await server.StatusAsync();

    public async Task<IActionResult> OnGetStatusAsync() =>
        this.Partial("/Pages/Admin/Server/_Status.cshtml", await server.StatusAsync());
}

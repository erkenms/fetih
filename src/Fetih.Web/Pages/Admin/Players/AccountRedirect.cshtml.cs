namespace Fetih.Web.Pages.Admin.Players;

using Fetih.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.AdminArea)]
public class AccountRedirectModel : PageModel
{
    public IActionResult OnGet(int? id) =>
        id is > 0 ? this.Redirect($"/admin/players/{id}") : this.Redirect("/admin/players");
}

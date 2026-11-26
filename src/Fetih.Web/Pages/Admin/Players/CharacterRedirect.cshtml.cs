namespace Fetih.Web.Pages.Admin.Players;

using Fetih.Web.Auth;
using Fetih.Web.Features.Players;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.AdminArea)]
public class CharacterRedirectModel(PlayerService players) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (id == null)
            return this.Redirect("/admin/players");
        if (id <= 0)
            return this.NotFound();

        uint? accountId = await players.AccountIdForCharacterAsync((uint)id.Value, ct);
        return accountId == null ? this.NotFound() : this.Redirect($"/admin/players/{accountId}");
    }
}

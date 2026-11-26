namespace Fetih.Web.Pages;

using Fetih.Web.Auth;
using Fetih.Web.Features.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class LoncaModel(PublicSiteService site) : PageModel
{
    public PublicGuildResponse Guild { get; private set; } = null!;

    public bool GameConnected => site.GameConnected;

    public async Task<IActionResult> OnGetAsync(string id)
    {
        if (!uint.TryParse(id, out uint guildId) || guildId == 0)
            return this.NotFound();

        var guild = await site.GetGuildAsync(guildId);
        if (guild == null)
            return this.NotFound();

        this.Guild = guild;
        return this.Page();
    }

    [Requires(Permission.ManageCharacters)]
    public async Task<IActionResult> OnPostDisbandAsync(string id, CancellationToken ct)
    {
        if (!uint.TryParse(id, out uint guildId) || guildId == 0)
            return this.NotFound();

        string? error = await site.DisbandGuildAsync(guildId, this.User, ct);
        if (error != null)
        {
            this.FlashError("Lonca bulunamadı.");
            return this.Redirect("/siralama/lonca");
        }

        this.Flash("Lonca dağıtıldı.");
        return this.Redirect("/siralama/lonca");
    }
}

namespace Fetih.Web.Pages;

using Fetih.Web.Features.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class KarakterModel(PublicSiteService site) : PageModel
{
    public PublicCharacterResponse Character { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id)
    {
        if (!uint.TryParse(id, out uint characterId) || characterId == 0)
            return this.NotFound();

        var ch = await site.GetCharacterAsync(characterId);
        if (ch == null)
            return this.NotFound();

        this.Character = ch;
        return this.Page();
    }
}

namespace Fetih.Web.Pages.Admin.Board;

using Fetih.Web.Auth;
using Fetih.Web.Features.Board;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.Moderate)]
public class IndexModel(BoardService board) : PageModel
{
    public ushort? Channel { get; private set; }

    public BoardPostView[] Posts { get; private set; } = [];

    public bool GameConnected => board.GameConnected;

    public async Task OnGetAsync(ushort? channel, CancellationToken ct)
    {
        if (channel is ushort c && BoardService.IsValidChannel(c))
            this.Channel = c;
        this.Posts = await board.ListAsync(this.Channel, ct);
    }

    [Requires(Permission.Moderate)]
    public async Task<IActionResult> OnPostDeleteAsync(
        ushort channel, uint characterId, ushort? currentChannel, CancellationToken ct)
    {
        if (await board.DeleteAsync(this.User, channel, characterId, ct) is { })
            this.FlashError("İleti bulunamadı; silinmiş olabilir.");
        else
            this.Flash("İleti silindi.");

        return currentChannel is ushort c && BoardService.IsValidChannel(c)
            ? this.RedirectToPage(new { channel = c })
            : this.RedirectToPage();
    }
}

namespace Fetih.Web.Pages;

using Fetih.Core.Logging;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

// Every error a page answers with: 403, 404, 500, and 503 when the game server is offline
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class HataModel : PageModel
{
    public int Code { get; private set; }

    public bool Admin { get; private set; }

    public string Title => this.Code switch
    {
        403 => "Yetkin yok",
        404 => "Sayfa bulunamadı",
        503 => "Oyun sunucusu kapalı",
        _ => "Bir hata oluştu",
    };

    public string Text => this.Code switch
    {
        403 => "Bu sayfayı açmaya yetkin yok.",
        404 => "Aradığın sayfa yok ya da taşındı.",
        503 => "İşlem yapılamadı; oyun sunucusu açılınca tekrar dene.",
        _ => "Sayfa açılırken bir hata oluştu. Kaydedildi; tekrar dene.",
    };

    public void OnGet(int code) => this.Show(code);

    public void OnPost(int code) => this.Show(code);

    private void Show(int code)
    {
        var failed = this.HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (failed?.Error is GameOfflineException)
            code = StatusCodes.Status503ServiceUnavailable;
        else if (failed != null)
            LogEvents.For<HataModel>(WebEvents.Error).Error(failed.Error,
                "Unhandled error on {Method} {Path}", this.Request.Method, failed.Path);

        this.Code = code;
        this.Response.StatusCode = code;
        string? path = failed?.Path ?? this.HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;
        this.Admin = path?.StartsWith("/admin") == true && this.User.Has(Permission.AdminArea);
    }
}

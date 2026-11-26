namespace Fetih.Web.Pages.Admin.Server;

using Fetih.Game.Admin;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Auth;
using Fetih.Web.Features.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ServerControl)]
public class IndexModel(ServerControlService server) : PageModel
{
    public const int MaxCountdownMinutes = ServerControl.MaxCountdownSeconds / 60;

    public ServerControlResponse Status { get; private set; } =
        new(false, false, "", "", false, null, null, "", 0, 0, null);

    public GuildWarInfo GuildWar { get; private set; } = new(null, null, null, false, null);

    public ShutdownMode ShutdownMode { get; private set; } = ShutdownMode.Restart;
    public int ShutdownMinutes { get; private set; } = 5;
    public string ShutdownReason { get; private set; } = "";
    public string? ShutdownError { get; private set; }

    public bool MaintenanceEnabled { get; private set; }
    public string MaintenanceMessage { get; private set; } = "";
    public bool KickNonGm { get; private set; }
    public string? MaintenanceError { get; private set; }

    public string MotdText { get; private set; } = "";
    public string? MotdError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await this.LoadAsync(ct);

    // Status box alone, refreshed by htmx every 5 seconds
    public async Task<IActionResult> OnGetStatusAsync() =>
        this.Partial("_Status", await server.StatusAsync());

    [Requires(Permission.ServerControl)]
    public async Task<IActionResult> OnPostStartShutdownAsync(
        ShutdownMode mode, int minutes, string? reason, CancellationToken ct)
    {
        int seconds = minutes is >= 1 and <= MaxCountdownMinutes ? minutes * 60 : 0;
        if (await server.StartShutdownAsync(this.User, mode, seconds, reason) is { } error)
        {
            this.ShutdownMode = mode;
            this.ShutdownMinutes = minutes;
            this.ShutdownReason = reason ?? "";
            this.ShutdownError = ErrorText(error);
            await this.LoadAsync(ct);
            return this.Page();
        }

        this.Flash("Geri sayım başlatıldı.");
        return this.RedirectToPage();
    }

    [Requires(Permission.ServerControl)]
    public async Task<IActionResult> OnPostCancelShutdownAsync()
    {
        if (await server.CancelShutdownAsync(this.User) is { } error)
            this.FlashError(ErrorText(error));
        else
            this.Flash("Geri sayım iptal edildi.");
        return this.RedirectToPage();
    }

    [Requires(Permission.ServerControl)]
    public async Task<IActionResult> OnPostMaintenanceAsync(
        bool enabled, string? message, bool kickNonGm, CancellationToken ct)
    {
        var (appliedLive, error) = await server.UpdateMaintenanceAsync(
            this.User, enabled, message, enabled && kickNonGm);
        if (error != null)
        {
            await this.LoadAsync(ct);
            this.MaintenanceEnabled = enabled;
            this.MaintenanceMessage = message ?? "";
            this.KickNonGm = kickNonGm;
            this.MaintenanceError = ErrorText(error);
            return this.Page();
        }

        this.Flash(appliedLive
            ? "Bakım ayarları canlı sunucuda güncellendi."
            : "Bakım ayarları kaydedildi; oyun sunucusu açıldığında geçerli olacak.");
        return this.RedirectToPage();
    }

    [Requires(Permission.ServerControl)]
    public async Task<IActionResult> OnPostMotdAsync(string? text, bool clear, CancellationToken ct)
    {
        string value = clear ? "" : text ?? "";
        var (appliedLive, error) = await server.UpdateMotdAsync(this.User, value);
        if (error != null)
        {
            await this.LoadAsync(ct);
            this.MotdText = value;
            this.MotdError = ErrorText(error);
            return this.Page();
        }

        this.Flash(appliedLive
            ? "MOTD canlı sunucuda güncellendi."
            : "MOTD kaydedildi; oyun sunucusu açıldığında geçerli olacak.");
        return this.RedirectToPage();
    }

    [Requires(Permission.ServerControl)]
    public async Task<IActionResult> OnPostStartGuildWarAsync()
    {
        if (await server.StartGuildWarAsync(this.User) is { } error)
            this.FlashError(ErrorText(error));
        else
            this.Flash("Lonca savaşı başladı.");
        return this.RedirectToPage();
    }

    [Requires(Permission.ServerControl)]
    public async Task<IActionResult> OnPostEndGuildWarAsync()
    {
        if (await server.EndGuildWarAsync(this.User) is { } error)
            this.FlashError(ErrorText(error));
        else
            this.Flash("Lonca savaşı bitti.");
        return this.RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        this.Status = await server.StatusAsync();
        this.GuildWar = await server.GuildWarStatusAsync(ct);
        this.MaintenanceEnabled = this.Status.MaintenanceEnabled;
        this.MaintenanceMessage = this.Status.MaintenanceMessage;
        this.MotdText = this.Status.Motd;
    }

    private static string ErrorText(string error) => error switch
    {
        "invalid_countdown" => $"Geri sayım 1 ile {MaxCountdownMinutes} dakika arasında olmalı.",
        "invalid_reason" => $"Sebep en çok {ServerControl.MaxReasonLength} karakter olabilir.",
        "shutdown_pending" => "Zaten devam eden bir geri sayım var.",
        "no_shutdown_pending" => "İptal edilecek bir geri sayım yok.",
        "invalid_message" => $"Bakım mesajı en çok {ServerControl.MaxMaintenanceMessageLength} karakter olabilir.",
        "invalid_motd" => $"Giriş mesajı en çok {ServerControl.MaxMotdLength} karakter olabilir.",
        "already_running" => "Lonca savaşı zaten sürüyor.",
        "not_running" => "Lonca savaşı sürmüyor.",
        _ => "İşlem başarısız: " + error,
    };
}

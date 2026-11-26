namespace Fetih.Web.Pages.Admin.Broadcasts;

using Fetih.Shared.Models.Admin;
using Fetih.Web.Auth;
using Fetih.Web.Features.Broadcasts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.Moderate)]
public class IndexModel(BroadcastService broadcasts) : PageModel
{
    public static readonly (BroadcastChannel Channel, string Text)[] Channels =
    [
        (BroadcastChannel.Center, "Orta"),
        (BroadcastChannel.System, "Sistem"),
        (BroadcastChannel.TopLeft, "Sol üst"),
    ];

    public AnnouncementDto[] Announcements { get; private set; } = [];

    public QueuedBroadcastItem[] Queue { get; private set; } = [];

    public bool GameConnected => broadcasts.GameConnected;

    // The announcement form that failed to save, shown again in its open dialog
    public AnnouncementForm? Failed { get; private set; }

    public string? FailedError { get; private set; }

    public async Task OnGetAsync() => await this.LoadAsync();

    // The paid queue alone, refreshed by htmx every 10 seconds
    public async Task<IActionResult> OnGetQueueAsync()
    {
        this.Queue = await broadcasts.QueueAsync();
        return this.Partial("_Queue", this);
    }

    [Requires(Permission.Moderate)]
    public async Task<IActionResult> OnPostSendAsync(BroadcastChannel channel, string? text)
    {
        if (await broadcasts.SendAsync(this.User, channel, text) is { } error)
            this.FlashError(ErrorText(error));
        else
            this.Flash("Duyuru gönderildi.");
        return this.RedirectToPage();
    }

    [Requires(Permission.Moderate)]
    public async Task<IActionResult> OnPostSaveAsync(AnnouncementForm form)
    {
        var request = new SaveAnnouncementRequest(form.Title ?? "", form.Channel, form.Message ?? "",
            form.IntervalMinutes, form.Enabled,
            form.StartAt == null ? null : WebTime.FromLocal(form.StartAt.Value),
            form.EndAt == null ? null : WebTime.FromLocal(form.EndAt.Value));
        var (saved, error) = await broadcasts.SaveAsync(this.User, form.Id == 0 ? null : form.Id, request);
        if (saved == null)
        {
            this.Failed = form;
            this.FailedError = ErrorText(error!);
            await this.LoadAsync();
            return this.Page();
        }

        this.Flash("Duyuru kaydedildi.");
        return this.RedirectToPage();
    }

    [Requires(Permission.Moderate)]
    public async Task<IActionResult> OnPostEnableAsync(uint id, bool enabled)
    {
        var (_, error) = await broadcasts.SetEnabledAsync(this.User, id, enabled);
        if (error != null)
            this.FlashError(ErrorText(error));
        else
            this.Flash(enabled ? "Duyuru açıldı." : "Duyuru kapatıldı.");
        return this.RedirectToPage();
    }

    [Requires(Permission.Moderate)]
    public async Task<IActionResult> OnPostDeleteAsync(uint id)
    {
        if (await broadcasts.DeleteAsync(id) is { } error)
            this.FlashError(ErrorText(error));
        else
            this.Flash("Duyuru silindi.");
        return this.RedirectToPage();
    }

    [Requires(Permission.Moderate)]
    public async Task<IActionResult> OnPostSendNowAsync(uint id)
    {
        var (_, error) = await broadcasts.SendNowAsync(this.User, id);
        if (error != null)
            this.FlashError(ErrorText(error));
        else
            this.Flash("Duyuru gönderildi.");
        return this.RedirectToPage();
    }

    public static string ChannelText(BroadcastChannel channel) =>
        Channels.FirstOrDefault(c => c.Channel == channel).Text ?? channel.ToString();

    private async Task LoadAsync()
    {
        this.Announcements = await broadcasts.ListAsync();
        this.Queue = await broadcasts.QueueAsync();
    }

    private static string ErrorText(string error) => error switch
    {
        "invalid_title" => $"Başlık boş olamaz ve en çok {BroadcastService.MaxTitleLength} karakter olabilir.",
        "invalid_message" => $"Mesaj boş olamaz ve en çok {BroadcastService.MaxMessageLength} karakter olabilir.",
        "invalid_interval" => $"Aralık 1 ile {BroadcastService.MaxIntervalMinutes} dakika arasında olmalı.",
        "invalid_schedule_window" => "Bitiş, başlangıçtan sonra olmalı.",
        "not_found" => "Duyuru bulunamadı; silinmiş olabilir.",
        _ => "Oyun sunucusu duyuruyu göndermedi: " + error,
    };

    // One announcement as the dialog edits it; Id 0 is a new one. Times are Turkey's.
    public sealed class AnnouncementForm
    {
        public uint Id { get; set; }
        public string? Title { get; set; }
        public BroadcastChannel Channel { get; set; } = BroadcastChannel.Center;
        public string? Message { get; set; }
        public uint IntervalMinutes { get; set; } = 30;
        public bool Enabled { get; set; } = true;
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }

        public static AnnouncementForm From(AnnouncementDto a) => new()
        {
            Id = a.Id, Title = a.Title, Channel = a.Channel, Message = a.Message,
            IntervalMinutes = a.IntervalMinutes, Enabled = a.Enabled,
            StartAt = a.StartAt == null ? null : WebTime.ToLocal(a.StartAt.Value),
            EndAt = a.EndAt == null ? null : WebTime.ToLocal(a.EndAt.Value),
        };
    }
}

namespace Fetih.Web.Pages.Admin.Players;

using Fetih.Account.Database.Models;
using Fetih.Game.States;
using Fetih.Web.Auth;
using Fetih.Web.Features.Accounts;
using Fetih.Web.Features.Characters;
using Fetih.Web.Features.Players;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.AdminArea)]
public class DetailModel(PlayerService players, WebConfiguration config) : PageModel
{
    [BindProperty(SupportsGet = true)] public uint Id { get; set; }
    [BindProperty(SupportsGet = true, Name = "sekme")] public string? Sekme { get; set; }

    public AccountDetailResponse Account { get; private set; } = null!;
    public CharacterDetailResponse? Character { get; private set; }
    public string ActiveTab { get; private set; } = "account";
    public string? OpenDialog { get; private set; }
    public string? DialogError { get; private set; }

    public string GrafanaEnv =>
        string.IsNullOrWhiteSpace(config.Observability.Environment) ? "dev" : config.Observability.Environment;

    public bool IsBanned => this.Account.StatusId == (ushort)AccountStatus.Banned;

    public bool IsMuted => this.Character?.MutedUntil is { } until && until > DateTime.UtcNow;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await this.LoadAsync(ct))
            return this.NotFound();
        return this.Page();
    }

    // Account actions

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostBanAsync(string? reason, CancellationToken ct) =>
        this.HandleAsync(() => players.BanAsync(this.Id, reason, this.User),
            "Hesap yasaklandı.", "dlg-ban", ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostUnbanAsync(string? reason, CancellationToken ct) =>
        this.HandleAsync(() => players.UnbanAsync(this.Id, reason, this.User),
            "Hesabın yasağı kaldırıldı.", "dlg-unban", ct);

    [Requires(Permission.ManageAccounts)]
    public Task<IActionResult> OnPostStatusAsync(ushort statusId, string? reason, CancellationToken ct) =>
        this.HandleAsync(() => players.SetStatusAsync(this.Id, statusId, reason, this.User, ct),
            "Hesap durumu güncellendi.", "dlg-status", ct);

    [Requires(Permission.ManageAccounts)]
    public Task<IActionResult> OnPostAuthorityAsync(ushort authorityId, CancellationToken ct) =>
        this.HandleAsync(() => players.SetAuthorityAsync(this.Id, authorityId, this.User, ct),
            "Hesap yetkisi güncellendi.", "dlg-authority", ct);

    [Requires(Permission.ManageAccounts)]
    public Task<IActionResult> OnPostResetPasswordAsync(string? newPassword, CancellationToken ct) =>
        this.HandleAsync(() => players.ResetPasswordAsync(this.Id, newPassword, ct),
            "Şifre güncellendi.", "dlg-password", ct);

    [Requires(Permission.ManageAccounts)]
    public Task<IActionResult> OnPostPacketTraceAsync(bool enabled, CancellationToken ct) =>
        this.HandleAsync(() => players.SetPacketTraceAsync(this.Id, enabled, this.User, ct),
            enabled ? "Paket izleme açıldı." : "Paket izleme kapatıldı.", null, ct);

    // Character actions

    [Requires(Permission.Moderate)]
    public Task<IActionResult> OnPostKickAsync(uint characterId, string? reason, CancellationToken ct) =>
        this.HandleAsync(() => players.KickAsync(characterId, reason, this.User),
            "Karakter oyundan atıldı.", "dlg-kick", ct);

    [Requires(Permission.Moderate)]
    public Task<IActionResult> OnPostMuteAsync(uint characterId, int minutes, CancellationToken ct) =>
        this.HandleAsync(() => players.MuteAsync(characterId, minutes, this.User),
            minutes > 0 ? $"Karakter {minutes} dakika susturuldu." : "Susturma kaldırıldı.", "dlg-mute", ct);

    [Requires(Permission.Moderate)]
    public Task<IActionResult> OnPostJailAsync(uint characterId, CancellationToken ct) =>
        this.HandleAsync(() => players.JailAsync(characterId, this.User),
            "Karakter hapse gönderildi.", null, ct);

    [Requires(Permission.Moderate)]
    public Task<IActionResult> OnPostReleaseAsync(uint characterId, CancellationToken ct) =>
        this.HandleAsync(() => players.ReleaseFromJailAsync(characterId, this.User, ct),
            "Karakter hapishaneden tahliye edildi.", null, ct);

    [Requires(Permission.Moderate)]
    public Task<IActionResult> OnPostMessageAsync(uint characterId, string? text, CancellationToken ct) =>
        this.HandleAsync(() => players.MessageAsync(characterId, text, this.User),
            "Özel mesaj gönderildi.", "dlg-message", ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostGiveItemAsync(
        uint characterId, uint typeId, int count, byte plus, byte bless, byte? gem1, byte? gem2, CancellationToken ct) =>
        this.HandleAsync(() => players.GiveItemAsync(
                characterId, new GiveItemRequest(typeId, Math.Clamp(count, 1, 40), plus, bless, gem1, gem2), this.User),
            "Eşya verildi.", "dlg-give-item", ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostTakeItemAsync(uint characterId, uint itemId, CancellationToken ct) =>
        this.HandleAsync(() => players.TakeItemAsync(characterId, itemId, this.User),
            "Eşya envanterden alındı.", null, ct, tab: "items");

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostSilverAsync(uint characterId, long amount, CancellationToken ct) =>
        this.HandleAsync(() => players.AdjustSilverAsync(characterId, amount, this.User),
            "Gümüş güncellendi.", "dlg-silver", ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostConquerPointsAsync(uint characterId, long amount, CancellationToken ct) =>
        this.HandleAsync(() => players.AdjustConquerPointsAsync(characterId, amount, this.User),
            "Conquer Puanı güncellendi.", "dlg-cp", ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostLevelAsync(uint characterId, byte level, CancellationToken ct) =>
        this.HandleAsync(() => players.SetLevelAsync(characterId, level, this.User),
            "Seviye güncellendi.", "dlg-level", ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostTeleportAsync(uint characterId, uint mapId, ushort x, ushort y, CancellationToken ct) =>
        this.HandleAsync(() => players.TeleportAsync(characterId, mapId, x, y, this.User),
            "Karakter ışınlandı.", "dlg-teleport", ct);

    private async Task<IActionResult> HandleAsync(
        Func<Task<string?>> action, string successMessage, string? dialogId, CancellationToken ct, string? tab = null)
    {
        string? error = await action();
        if (error == null)
        {
            this.Flash(successMessage);
            return this.RedirectToPage(new { id = this.Id, sekme = tab ?? this.Sekme });
        }

        if (error == "not_found")
            return this.NotFound();
        if (error == "outranked")
            return this.Forbid();

        if (dialogId != null && await this.LoadAsync(ct))
        {
            this.OpenDialog = dialogId;
            this.DialogError = PlayerText.Error(error);
            return this.Page();
        }

        this.FlashError(PlayerText.Error(error));
        return this.RedirectToPage(new { id = this.Id, sekme = tab ?? this.Sekme });
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        if (this.Id == 0)
            return false;

        var account = await players.GetAccountAsync(this.Id, ct);
        if (account == null)
            return false;
        this.Account = account;

        if (account.Characters.Length > 0)
            this.Character = await players.GetCharacterAsync(account.Characters[0].CharacterId, ct);

        bool hasChar = this.Character != null;
        string requested = (this.Sekme ?? "").Trim().ToLowerInvariant();
        this.ActiveTab = requested switch
        {
            "profile" when hasChar => "profile",
            "items" when hasChar => "items",
            "skills" when hasChar => "skills",
            "relations" when hasChar => "relations",
            "account" => "account",
            "logins" => "logins",
            _ => hasChar ? "profile" : "account",
        };
        return true;
    }
}

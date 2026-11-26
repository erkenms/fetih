namespace Fetih.Web.Pages;

using Fetih.Web.Auth;
using Fetih.Web.Features.PublicAccount;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires]
public class HesabimModel(PublicAccountService accounts) : PageModel
{
    public MyAccountResponse Profile { get; private set; } = null!;

    public PublicAccountConfigResponse Config { get; private set; } = null!;

    public string? PasswordError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await this.LoadAsync(ct))
            return this.NotFound();
        return this.Page();
    }

    [Requires]
    public async Task<IActionResult> OnPostPasswordAsync(
        string? mevcutSifre, string? yeniSifre, string? yeniSifreTekrar, CancellationToken ct)
    {
        this.Config = accounts.GetConfig();

        string? error = null;
        if (string.IsNullOrEmpty(mevcutSifre))
            error = "Mevcut şifre gerekli.";
        else if (string.IsNullOrEmpty(yeniSifre))
            error = "Yeni şifre gerekli.";
        else if (yeniSifre != yeniSifreTekrar)
            error = "Yeni şifreler eşleşmiyor.";
        else
        {
            string? result = await accounts.ChangePasswordAsync(this.User.AccountId(), mevcutSifre, yeniSifre);
            error = result switch
            {
                null => null,
                "wrong_password" => "Mevcut şifre hatalı.",
                "invalid_password" => $"Şifre {this.Config.MinPasswordLength}–{this.Config.MaxPasswordLength} ASCII karakter olmalıdır.",
                "too_many_attempts" => "Çok fazla deneme; bir dakika sonra tekrar dene.",
                "unauthorized" => "Oturumun geçersiz. Lütfen yeniden giriş yap.",
                _ => "Şifre güncellenemedi.",
            };
        }

        if (error != null)
        {
            this.PasswordError = error;
            if (!await this.LoadAsync(ct))
                return this.NotFound();
            return this.Page();
        }

        this.Flash("Şifren güncellendi.");
        return this.RedirectToPage();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        this.Config = accounts.GetConfig();
        var profile = await accounts.GetProfileAsync(this.User.AccountId(), ct);
        if (profile == null)
            return false;
        this.Profile = profile;
        return true;
    }
}

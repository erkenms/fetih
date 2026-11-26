namespace Fetih.Web.Pages;

using Fetih.Web.Features.PublicAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class KayitModel(PublicAccountService accounts) : PageModel
{
    [BindProperty]
    public string? Eposta { get; set; }

    [BindProperty]
    public string? Sifre { get; set; }

    [BindProperty]
    public string? SifreTekrar { get; set; }

    public PublicAccountConfigResponse Config { get; private set; } = null!;

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (this.User.Identity?.IsAuthenticated == true)
            return this.Redirect("/hesabim");

        this.Config = accounts.GetConfig();
        return this.Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        this.Config = accounts.GetConfig();
        if (this.User.Identity?.IsAuthenticated == true)
            return this.Redirect("/hesabim");

        string email = (this.Eposta ?? "").Trim();
        this.Eposta = email;

        if (email.Length == 0)
        {
            this.Error = "E-posta adresi gerekli.";
            return this.Page();
        }
        if (string.IsNullOrEmpty(this.Sifre))
        {
            this.Error = "Şifre gerekli.";
            return this.Page();
        }
        if (this.Sifre != this.SifreTekrar)
        {
            this.Error = "Şifreler eşleşmiyor.";
            return this.Page();
        }

        string? captchaToken = this.Request.Form["cf-turnstile-response"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(captchaToken))
            captchaToken = this.Request.Form["captchaToken"].FirstOrDefault();

        var (account, error) = await accounts.RegisterAsync(this.HttpContext, email, this.Sifre, captchaToken, ct);
        if (account == null || error != null)
        {
            this.Error = error switch
            {
                "username_taken" => "Bu e-posta adresiyle kayıtlı bir hesap zaten var.",
                "invalid_username" => $"Geçerli bir ASCII e-posta adresi girin (en fazla {this.Config.MaxUsernameLength} karakter).",
                "invalid_password" => $"Şifre {this.Config.MinPasswordLength}–{this.Config.MaxPasswordLength} ASCII karakter olmalıdır.",
                "captcha_required" => "Lütfen doğrulama adımını tamamlayın.",
                "captcha_failed" => "Doğrulama başarısız oldu. Lütfen tekrar deneyin.",
                "too_many_attempts" => "Çok fazla deneme; bir dakika sonra tekrar dene.",
                "staff_only" => "Bu sunucuya şu anda yalnızca yetkililer kayıt olabilir.",
                _ => "Kayıt tamamlanamadı.",
            };
            return this.Page();
        }

        this.Flash("Hesabın oluşturuldu.");
        return this.Redirect("/hesabim");
    }
}

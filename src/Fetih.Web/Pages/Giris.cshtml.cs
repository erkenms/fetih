namespace Fetih.Web.Pages;

using Fetih.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class GirisModel(SignIn signIn) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Donus { get; set; }

    [BindProperty]
    public string? Kullanici { get; set; }

    [BindProperty]
    public string? Sifre { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        string? refusal = await signIn.TryAsync(this.HttpContext, this.Kullanici, this.Sifre);
        this.Error = refusal switch
        {
            null => null,
            "too_many_attempts" => "Çok fazla hatalı deneme. Bir dakika sonra tekrar dene.",
            "banned" => "Bu hesap yasaklı.",
            "locked" => "Bu hesap kilitli.",
            "staff_only" => "Bu sunucuya yalnızca yetkililer girebilir.",
            _ => "Hesap adı ya da şifre yanlış.",
        };
        if (this.Error != null)
        {
            this.Response.StatusCode = refusal == "too_many_attempts"
                ? StatusCodes.Status429TooManyRequests : StatusCodes.Status200OK;
            return this.Page();
        }

        if (this.Url.IsLocalUrl(this.Donus))
            return this.LocalRedirect(this.Donus);
        return this.Redirect(this.User.Has(Permission.AdminArea) ? "/admin" : "/");
    }

    public async Task<IActionResult> OnPostCikisAsync()
    {
        await Auth.SignIn.SignOutAsync(this.HttpContext);
        return this.Redirect("/");
    }
}

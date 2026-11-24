namespace Comet.Web.Features.PublicAccount;

public sealed class PublicAccountOptions
{
    public int RegisterAttemptsPerMinute { get; set; } = 5;
    public int PasswordChangesPerMinute { get; set; } = 5;
    public bool CaptchaEnabled { get; set; }
    public string CaptchaProvider { get; set; } = "turnstile";
    public string? CaptchaSiteKey { get; set; }
    public string? CaptchaSecretKey { get; set; }
    public string CaptchaVerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

namespace Fetih.Web.Features.PublicAccount;

public record PublicAccountConfigResponse(
    bool CaptchaEnabled,
    string? CaptchaProvider,
    string? CaptchaSiteKey,
    int MinPasswordLength,
    int MaxPasswordLength,
    int MaxUsernameLength);

public record MyCharacterSummary(
    uint CharacterId,
    string Name,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    uint MapId,
    uint Silver,
    uint Jewels,
    uint VipLevel,
    string Spouse,
    DateTime Registered);

public record LoginHistoryEntry(DateTime Timestamp, string IpAddress);

public record MyAccountResponse(
    uint AccountId,
    string Username,
    int Authority,
    int Status,
    DateTime Registered,
    MyCharacterSummary[] Characters,
    LoginHistoryEntry[] Logins);

public sealed class PublicAccountFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddOptions<PublicAccountOptions>().BindConfiguration("PublicAccount");
        services.AddHttpClient("captcha");
        services.AddSingleton<ICaptchaVerifier, HttpCaptchaVerifier>();
        services.AddSingleton<PublicAccountService>();
    }
}

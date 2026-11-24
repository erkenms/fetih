namespace Comet.Web.Features.PublicAccount;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct);
}

public sealed class HttpCaptchaVerifier(
    IOptions<PublicAccountOptions> options,
    IHttpClientFactory httpClientFactory) : ICaptchaVerifier
{
    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.CaptchaEnabled)
            return true;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(settings.CaptchaSecretKey))
            return false;

        var fields = new Dictionary<string, string>
        {
            ["secret"] = settings.CaptchaSecretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
            fields["remoteip"] = remoteIp;

        try
        {
            using var client = httpClientFactory.CreateClient("captcha");
            using var content = new FormUrlEncodedContent(fields);
            using var response = await client.PostAsync(settings.CaptchaVerifyUrl, content, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
            return body?.Success == true;
        }
        catch
        {
            return false;
        }
    }
}

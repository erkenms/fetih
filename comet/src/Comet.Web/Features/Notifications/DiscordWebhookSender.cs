namespace Comet.Web.Features.Notifications;

using System.Net.Http.Json;
using System.Text.RegularExpressions;

public record DiscordEmbedField(string Name, string Value, bool Inline = true);

public record DiscordEmbed(
    string Title,
    string Description,
    int Color,
    DateTime TimestampUtc,
    DiscordEmbedField[]? Fields = null);

public record WebhookSendResult(bool Ok, int? StatusCode, string? Error);

public interface IDiscordWebhookSender
{
    Task<WebhookSendResult> SendAsync(string webhookUrl, DiscordEmbed embed, CancellationToken ct);
}

public sealed partial class DiscordWebhookSender(HttpClient http) : IDiscordWebhookSender
{
    public const int ColorBan = 0xC0392B;
    public const int ColorError = 0xE74C3C;
    public const int ColorCheatReport = 0xE67E22;
    public const int ColorTest = 0xC9A227;

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "discordapp.com",
        "ptb.discord.com",
        "canary.discord.com",
    };

    [GeneratedRegex(@"^/api(?:/v\d+)?/webhooks/\d{15,22}/[A-Za-z0-9._-]{20,}$")]
    private static partial Regex WebhookPathRegex();

    public static bool IsValidDiscordWebhookUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 512)
            return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!AllowedHosts.Contains(uri.Host))
            return false;
        return WebhookPathRegex().IsMatch(uri.AbsolutePath);
    }

    public static string MaskWebhookUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "••••••";
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return $"{uri.Scheme}://{uri.Host}/••••••";
        string token = segments[^1];
        string tail = token.Length > 4 ? token[^4..] : "";
        segments[^1] = "••••••" + tail;
        return $"{uri.Scheme}://{uri.Host}/{string.Join('/', segments)}";
    }

    public async Task<WebhookSendResult> SendAsync(string webhookUrl, DiscordEmbed embed, CancellationToken ct)
    {
        var payload = new
        {
            username = "Fetih",
            embeds = new[]
            {
                new
                {
                    title = Truncate(embed.Title, 256),
                    description = Truncate(embed.Description, 4000),
                    color = embed.Color,
                    timestamp = DateTime.SpecifyKind(embed.TimestampUtc, DateTimeKind.Utc).ToString("O"),
                    fields = embed.Fields?.Select(f => new
                    {
                        name = Truncate(f.Name, 256),
                        value = Truncate(f.Value, 1024),
                        inline = f.Inline,
                    }).ToArray(),
                },
            },
        };

        try
        {
            using var response = await http.PostAsJsonAsync(webhookUrl, payload, ct);
            if (response.IsSuccessStatusCode)
                return new WebhookSendResult(true, (int)response.StatusCode, null);

            string body = await response.Content.ReadAsStringAsync(ct);
            return new WebhookSendResult(false, (int)response.StatusCode,
                Truncate(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "HTTP error" : body, 255));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new WebhookSendResult(false, null, Truncate(ex.Message, 255));
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}

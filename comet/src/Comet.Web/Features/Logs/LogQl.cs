namespace Comet.Web.Features.Logs;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public record LogQueryFilter(
    uint? CharacterId = null,
    uint? AccountId = null,
    string? Server = null,
    string? Level = null,
    string? EventPrefix = null,
    string? Search = null,
    bool IncludePackets = false);

public static class LogQl
{
    private static readonly HashSet<string> AllowedServers = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "account", "web",
    };

    private static readonly HashSet<string> AllowedLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "trace", "debug", "info", "warn", "error", "fatal",
    };

    private static readonly Regex SafeEventPrefix = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    public static string BuildQuery(LogQueryFilter filter)
    {
        var selectors = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.Server) && AllowedServers.Contains(filter.Server.Trim()))
            selectors.Add($"server=\"{filter.Server.Trim().ToLowerInvariant()}\"");
        else
            selectors.Add("server=~\".+\"");

        if (!string.IsNullOrWhiteSpace(filter.Level) && AllowedLevels.Contains(filter.Level.Trim()))
            selectors.Add($"level=\"{filter.Level.Trim().ToLowerInvariant()}\"");

        string? prefix = filter.EventPrefix?.Trim();
        if (!string.IsNullOrEmpty(prefix) && SafeEventPrefix.IsMatch(prefix))
            selectors.Add($"event=~\"^{EscapeRegex(prefix)}.*\"");
        else if (!filter.IncludePackets)
            selectors.Add("event!~\"packet\\\\.(in|out)\"");

        var sb = new StringBuilder();
        sb.Append('{').Append(string.Join(", ", selectors)).Append('}');

        if (filter.CharacterId != null || filter.AccountId != null)
        {
            sb.Append(" | json");
            if (filter.CharacterId != null)
                sb.Append(" | characterId=\"").Append(filter.CharacterId.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (filter.AccountId != null)
                sb.Append(" | accountId=\"").Append(filter.AccountId.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        string? search = SanitizeLineFilter(filter.Search);
        if (!string.IsNullOrEmpty(search))
            sb.Append(" |= \"").Append(search).Append('"');

        return sb.ToString();
    }

    public static string BuildGrafanaUrl(string grafanaBaseUrl, LogQueryFilter filter, DateTime fromUtc, DateTime toUtc)
    {
        string baseUrl = grafanaBaseUrl.TrimEnd('/');
        long fromMs = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        long toMs = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        if (filter.CharacterId != null || filter.AccountId != null)
        {
            string charVar = filter.CharacterId?.ToString(CultureInfo.InvariantCulture) ?? ".*";
            string accVar = filter.AccountId?.ToString(CultureInfo.InvariantCulture) ?? ".*";
            return $"{baseUrl}/d/comet-player/player?var-characterId={Uri.EscapeDataString(charVar)}&var-accountId={Uri.EscapeDataString(accVar)}&from={fromMs}&to={toMs}";
        }

        return $"{baseUrl}/d/comet-overview/overview?from={fromMs}&to={toMs}";
    }

    public static string BuildGrafanaExploreUrl(string grafanaBaseUrl, string logQl, DateTime fromUtc, DateTime toUtc)
    {
        string baseUrl = grafanaBaseUrl.TrimEnd('/');
        long fromMs = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        long toMs = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        var panes = new Dictionary<string, object>
        {
            ["timeline"] = new
            {
                datasource = "loki",
                queries = new[]
                {
                    new { refId = "A", expr = logQl, queryType = "range", datasource = new { type = "loki", uid = "loki" } },
                },
                range = new
                {
                    from = fromMs.ToString(CultureInfo.InvariantCulture),
                    to = toMs.ToString(CultureInfo.InvariantCulture),
                },
            },
        };
        string encoded = Uri.EscapeDataString(JsonSerializer.Serialize(panes));
        return $"{baseUrl}/explore?schemaVersion=1&panes={encoded}";
    }

    private static string EscapeRegex(string value) =>
        value.Replace("\\", "\\\\").Replace(".", "\\.");

    private static string? SanitizeLineFilter(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        string cleaned = search.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

namespace Fetih.Web.Features.Logs;

using System.Globalization;
using System.Text.Json;
using Fetih.Web.Features.Dashboard;

public sealed class LokiUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public record LogEntry(
    DateTime Timestamp,
    string Server,
    string Level,
    string Event,
    string Message,
    uint? CharacterId,
    uint? AccountId,
    string? Source,
    string? Exception,
    Dictionary<string, string> Fields);

public interface ILokiClient
{
    Task<LogEntry[]> QueryRangeAsync(string logQl, DateTime startUtc, DateTime endUtc, int limit, CancellationToken ct);
}

public sealed class LokiClient(HttpClient http, ObservabilityOptions options) : ILokiClient
{
    private static readonly HashSet<string> ReservedJsonFields = new(StringComparer.Ordinal)
    {
        "v", "ts", "server", "level", "event", "msg", "src", "exc", "characterId", "accountId",
    };

    public async Task<LogEntry[]> QueryRangeAsync(string logQl, DateTime startUtc, DateTime endUtc, int limit, CancellationToken ct)
    {
        long startNs = ToUnixNanoseconds(startUtc);
        long endNs = ToUnixNanoseconds(endUtc);
        string baseUrl = options.LokiUrl.TrimEnd('/');
        string url = $"{baseUrl}/loki/api/v1/query_range?query={Uri.EscapeDataString(options.ScopeLogs(logQl))}&start={startNs}&end={endNs}&limit={limit}&direction=backward";

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new LokiUnavailableException("Loki is unreachable", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            throw new LokiUnavailableException($"Loki returned {(int)response.StatusCode}: {body}");
        }

        string json = await response.Content.ReadAsStringAsync(ct);
        return ParseResponse(json);
    }

    internal static LogEntry[] ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return [];

        var entries = new List<LogEntry>();
        foreach (var streamObj in results.EnumerateArray())
        {
            string streamServer = "";
            string streamLevel = "";
            string streamEvent = "";
            if (streamObj.TryGetProperty("stream", out var streamLabels) && streamLabels.ValueKind == JsonValueKind.Object)
            {
                streamServer = GetString(streamLabels, "server") ?? "";
                streamLevel = GetString(streamLabels, "level") ?? "";
                streamEvent = GetString(streamLabels, "event") ?? "";
            }

            if (!streamObj.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var pair in values.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                    continue;
                string? nsText = pair[0].GetString();
                string? line = pair[1].GetString();
                if (nsText == null || line == null)
                    continue;

                DateTime timestamp = FromUnixNanoseconds(nsText);
                entries.Add(ParseLine(timestamp, streamServer, streamLevel, streamEvent, line));
            }
        }

        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries.ToArray();
    }

    private static LogEntry ParseLine(DateTime fallbackTimestamp, string streamServer, string streamLevel, string streamEvent, string line)
    {
        try
        {
            using var lineDoc = JsonDocument.Parse(line);
            var root = lineDoc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new LogEntry(fallbackTimestamp, streamServer, streamLevel, streamEvent, line, null, null, null, null, new());

            DateTime ts = fallbackTimestamp;
            if (GetString(root, "ts") is { } tsText &&
                DateTime.TryParse(tsText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedTs))
                ts = parsedTs;

            string server = GetString(root, "server") ?? streamServer;
            string level = GetString(root, "level") ?? streamLevel;
            string evt = GetString(root, "event") ?? streamEvent;
            string msg = GetString(root, "msg") ?? line;
            string? src = GetString(root, "src");
            string? exc = GetString(root, "exc");
            uint? characterId = GetUInt32(root, "characterId");
            uint? accountId = GetUInt32(root, "accountId");

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in root.EnumerateObject())
            {
                if (ReservedJsonFields.Contains(prop.Name))
                    continue;
                fields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
            }

            return new LogEntry(ts, server, level, evt, msg, characterId, accountId, src, exc, fields);
        }
        catch (JsonException)
        {
            return new LogEntry(fallbackTimestamp, streamServer, streamLevel, streamEvent, line, null, null, null, null, new());
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static uint? GetUInt32(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetUInt32(out uint num))
            return num;
        if (prop.ValueKind == JsonValueKind.String && uint.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out num))
            return num;
        return null;
    }

    private static long ToUnixNanoseconds(DateTime utc)
    {
        var dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return dto.ToUnixTimeMilliseconds() * 1_000_000L;
    }

    private static DateTime FromUnixNanoseconds(string nsText)
    {
        if (!long.TryParse(nsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ns))
            return DateTime.UtcNow;
        long ms = ns / 1_000_000L;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }
}

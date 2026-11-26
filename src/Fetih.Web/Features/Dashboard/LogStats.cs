namespace Fetih.Web.Features.Dashboard;

using System.Globalization;
using System.Text.Json;
using Fetih.Web.Features.Logs;

// The "Observability" config section, shared with the log timelines
public sealed class ObservabilityOptions
{
    public string LokiUrl { get; set; } = "http://127.0.0.1:3100";
    public string GrafanaUrl { get; set; } = "http://localhost:3000";
    public string PrometheusUrl { get; set; } = "http://127.0.0.1:9090";

    // prod or test: this web reads only its own environment's logs and metrics, plus the shared
    // account server's logs (env "shared"). Unset (development) reads everything.
    public string? Environment { get; set; }

    public string ScopeLogs(string logQl) => LogQl.Scope(logQl, this.Environment);

    public string ScopeMetric(string metric) =>
        string.IsNullOrWhiteSpace(this.Environment) ? metric : $"{metric}{{env=\"{this.Environment}\"}}";
}

public record LogLine(DateTime Timestamp, string Server, string Level, string Event, string Message);

public record LoginLine(DateTime Timestamp, uint AccountId, uint CharacterId, string Name, int Level,
    string IpAddress, uint MapId);

public record LogStatsResponse(int Errors, int Warnings, LogLine[] RecentErrors, int Logins,
    int Accounts, LoginLine[] RecentLogins);

// What the dashboard reads from Loki: warnings and errors in the last hour with the latest
// error lines, and logins in the last day with the latest ones. Null when Loki can't be
// reached: the dashboard then shows the rest without them.
public sealed class LogStats(HttpClient http, ObservabilityOptions options)
{
    public const string ProblemSelector = "{level=~\"warn|error|fatal\", event!=\"packet.unhandled\"}";
    public const string ErrorSelector = "{level=~\"error|fatal\"}";

    public const string LoginSelector = "{event=\"session.login\"}";

    public async Task<LogStatsResponse?> GetAsync(TimeSpan errorWindow, TimeSpan loginWindow, int recent,
        CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            var errorRange = $"{(int)errorWindow.TotalMinutes}m";
            var loginRange = $"{(int)loginWindow.TotalMinutes}m";
            var counts = this.InstantAsync($"sum by (level) (count_over_time({ProblemSelector}[{errorRange}]))", now, ct);
            var errors = this.RangeAsync(ErrorSelector, now - errorWindow, now, recent, ct);
            var logins = this.InstantAsync($"sum(count_over_time({LoginSelector}[{loginRange}]))", now, ct);
            var accounts = this.InstantAsync(
                $"count(sum by (accountId) (count_over_time({LoginSelector} | json accountId [{loginRange}])))", now, ct);
            var loginLines = this.RangeAsync(LoginSelector, now - loginWindow, now, recent, ct);

            using var countsDoc = await counts;
            using var errorsDoc = await errors;
            using var loginsDoc = await logins;
            using var accountsDoc = await accounts;
            using var loginLinesDoc = await loginLines;
            var byLevel = ParseCounts(countsDoc, "level");
            return new LogStatsResponse(
                byLevel.GetValueOrDefault("error") + byLevel.GetValueOrDefault("fatal"),
                byLevel.GetValueOrDefault("warn"),
                ParseLines(errorsDoc).Take(recent).ToArray(),
                ParseCounts(loginsDoc, "").GetValueOrDefault(""),
                ParseCounts(accountsDoc, "").GetValueOrDefault(""),
                ParseLogins(loginLinesDoc).Take(recent).ToArray());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private Task<JsonDocument> InstantAsync(string query, DateTime at, CancellationToken ct) =>
        this.GetJsonAsync($"/loki/api/v1/query?query={Uri.EscapeDataString(options.ScopeLogs(query))}&time={Nanoseconds(at)}", ct);

    private Task<JsonDocument> RangeAsync(string query, DateTime from, DateTime to, int limit, CancellationToken ct) =>
        this.GetJsonAsync($"/loki/api/v1/query_range?query={Uri.EscapeDataString(options.ScopeLogs(query))}" +
            $"&start={Nanoseconds(from)}&end={Nanoseconds(to)}&limit={limit}&direction=backward", ct);

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(options.LokiUrl.TrimEnd('/') + path, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    // A vector result: [{"metric": {"level": "error"}, "value": [time, "12"]}], summed per
    // value of `label` ("" for one total)
    internal static Dictionary<string, int> ParseCounts(JsonDocument doc, string label)
    {
        var counts = new Dictionary<string, int>();
        foreach (var sample in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
        {
            var level = sample.GetProperty("metric").TryGetProperty(label, out var l) ? l.GetString() ?? "" : "";
            var value = sample.GetProperty("value")[1].GetString();
            counts[level] = counts.GetValueOrDefault(level) +
                (int)double.Parse(value ?? "0", CultureInfo.InvariantCulture);
        }
        return counts;
    }

    // A streams result: [{"stream": {labels}, "values": [["ns", "json line"]]}], newest first
    internal static IEnumerable<LogLine> ParseLines(JsonDocument doc) =>
        Entries(doc).Select(e => new LogLine(e.Timestamp, e.Label("server"), e.Label("level"), e.Label("event"),
            e.Line?.RootElement.TryGetProperty("msg", out var msg) == true ? msg.GetString() ?? e.Text : e.Text));

    // session.login lines carry name, accountId, characterId, _level, ip and mapId
    internal static IEnumerable<LoginLine> ParseLogins(JsonDocument doc) =>
        Entries(doc).Where(e => e.Line != null).Select(e =>
        {
            var line = e.Line!.RootElement;
            string Text(string name) => line.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            long Number(string name) => line.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
            return new LoginLine(e.Timestamp, (uint)Number("accountId"), (uint)Number("characterId"), Text("name"),
                (int)Number("_level"), Text("ip"), (uint)Number("mapId"));
        });

    private sealed record Entry(DateTime Timestamp, JsonElement Labels, string Text, JsonDocument? Line)
    {
        public string Label(string name) => this.Labels.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
    }

    private static IEnumerable<Entry> Entries(JsonDocument doc)
    {
        var entries = new List<Entry>();
        foreach (var stream in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
        foreach (var value in stream.GetProperty("values").EnumerateArray())
        {
            var text = value[1].GetString() ?? "";
            JsonDocument? line = null;
            try { line = JsonDocument.Parse(text); }
            catch (JsonException) { }
            entries.Add(new Entry(DateTime.UnixEpoch.AddTicks(long.Parse(value[0].GetString()!, CultureInfo.InvariantCulture) / 100),
                stream.GetProperty("stream"), text, line));
        }
        return entries.OrderByDescending(e => e.Timestamp);
    }

    private static long Nanoseconds(DateTime utc) => (utc - DateTime.UnixEpoch).Ticks * 100;
}

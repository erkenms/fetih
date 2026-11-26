namespace Comet.Web.Features.Logs;

using System.Globalization;
using System.Text.Json;
using Comet.Web.Features.Dashboard;

public sealed record TrendPoint(DateTime Timestamp, double Value);

public sealed class MetricsUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

// Range queries against Prometheus and Loki's metric queries, which answer in the same matrix
// format: one series per label value, each a list of [unix seconds, "value"]
public interface IMetricRanges
{
    Task<Dictionary<string, TrendPoint[]>> PrometheusAsync(string query, string label, DateTime start, DateTime end,
        TimeSpan step, CancellationToken ct);

    Task<Dictionary<string, TrendPoint[]>> LokiAsync(string query, string label, DateTime start, DateTime end,
        TimeSpan step, CancellationToken ct);
}

public sealed class MetricRanges(HttpClient http, ObservabilityOptions options) : IMetricRanges
{
    public Task<Dictionary<string, TrendPoint[]>> PrometheusAsync(string query, string label, DateTime start,
        DateTime end, TimeSpan step, CancellationToken ct) =>
        this.RangeAsync(options.PrometheusUrl, "/api/v1/query_range", query, label, start, end, step, ct);

    public Task<Dictionary<string, TrendPoint[]>> LokiAsync(string query, string label, DateTime start,
        DateTime end, TimeSpan step, CancellationToken ct) =>
        this.RangeAsync(options.LokiUrl, "/loki/api/v1/query_range", options.ScopeLogs(query), label, start, end, step, ct);

    private async Task<Dictionary<string, TrendPoint[]>> RangeAsync(string baseUrl, string path, string query,
        string label, DateTime start, DateTime end, TimeSpan step, CancellationToken ct)
    {
        string url = $"{baseUrl.TrimEnd('/')}{path}?query={Uri.EscapeDataString(query)}" +
            $"&start={Seconds(start)}&end={Seconds(end)}&step={(int)Math.Max(step.TotalSeconds, 15)}";
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                throw new MetricsUnavailableException($"{baseUrl} returned {(int)response.StatusCode}: " +
                    await response.Content.ReadAsStringAsync(ct));
            return ParseMatrix(await response.Content.ReadAsStringAsync(ct), label);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new MetricsUnavailableException($"{baseUrl} is unreachable", e);
        }
    }

    // Series keyed by the value of `label` ("" when the series doesn't have it)
    internal static Dictionary<string, TrendPoint[]> ParseMatrix(string json, string label)
    {
        var series = new Dictionary<string, TrendPoint[]>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return series;

        foreach (var result in results.EnumerateArray())
        {
            string key = result.TryGetProperty("metric", out var metric) &&
                metric.TryGetProperty(label, out var value) ? value.GetString() ?? "" : "";
            if (!result.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
                continue;
            var points = new List<TrendPoint>();
            foreach (var pair in values.EnumerateArray())
            {
                if (pair.GetArrayLength() < 2 ||
                    !double.TryParse(pair[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ||
                    double.IsNaN(v) || double.IsInfinity(v))
                    continue;
                points.Add(new TrendPoint(DateTimeOffset.FromUnixTimeMilliseconds((long)(pair[0].GetDouble() * 1000)).UtcDateTime, v));
            }
            series[key] = points.ToArray();
        }
        return series;
    }

    private static string Seconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}

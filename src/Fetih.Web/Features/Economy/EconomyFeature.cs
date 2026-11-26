namespace Fetih.Web.Features.Economy;

using Fetih.Web.Features.Dashboard;
using Fetih.Web.Features.Logs;

using Fetih.Game.Database;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class EconomyFeature : IFeature
{
    public const int DefaultTop = 15;
    public const int MaxTop = 50;

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddHttpClient<IMetricRanges, MetricRanges>(client => client.Timeout = TimeSpan.FromSeconds(5));
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/economy").RequirePermission(Permission.AdminArea);
        group.MapGet("", GetEconomyAsync).WithName("getEconomy");
    }

    private static async Task<Ok<EconomyResponse>> GetEconomyAsync(
        IMetricRanges metrics,
        ObservabilityOptions options,
        string range = "24h",
        int top = DefaultTop,
        CancellationToken ct = default)
    {
        (string rangeKey, TimeSpan window, TimeSpan step) = NormalizeRange(range);
        int topCount = Math.Clamp(top, 1, MaxTop);
        DateTime now = DateTime.UtcNow;
        DateTime startUtc = now - window;

        var trendsTask = QueryTrendsAsync(metrics, options, startUtc, now, step, ct);
        var flowTask = QueryFlowAsync(metrics, startUtc, now, step, ct);
        var (totals, topSilver, topCp) = await QueryDatabaseAsync(topCount, ct);

        return TypedResults.Ok(new EconomyResponse(now, rangeKey, totals, topSilver, topCp,
            await trendsTask, await flowTask));
    }

    private static (string Key, TimeSpan Window, TimeSpan Step) NormalizeRange(string? range) =>
        range switch
        {
            "1h" => ("1h", TimeSpan.FromHours(1), TimeSpan.FromMinutes(5)),
            "7d" => ("7d", TimeSpan.FromDays(7), TimeSpan.FromHours(6)),
            "30d" => ("30d", TimeSpan.FromDays(30), TimeSpan.FromDays(1)),
            _ => ("24h", TimeSpan.FromHours(24), TimeSpan.FromHours(1)),
        };

    // Online characters count as of their last save
    private static async Task<(EconomyTotals Totals, TopHolderEntry[] TopSilver, TopHolderEntry[] TopCp)> QueryDatabaseAsync(
        int topCount, CancellationToken ct)
    {
        await using var db = new ServerDbContext();

        var sums = await db.Characters.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Pocket = g.Sum(c => (long)c.Silver),
                Stored = g.Sum(c => (long)c.StoredSilver),
                Cp = g.Sum(c => (long)c.Jewels),
                Count = g.Count(),
            })
            .FirstOrDefaultAsync(ct);
        var funds = await db.Syndicates.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Silver = g.Sum(s => (long)s.Fund), Cp = g.Sum(s => (long)s.ConquerPointsFund) })
            .FirstOrDefaultAsync(ct);

        ulong pocket = (ulong)(sums?.Pocket ?? 0), stored = (ulong)(sums?.Stored ?? 0);
        var totals = new EconomyTotals(pocket + stored, pocket, stored, (ulong)(sums?.Cp ?? 0),
            (ulong)(funds?.Silver ?? 0), (ulong)(funds?.Cp ?? 0), sums?.Count ?? 0);

        var holders = db.Characters.AsNoTracking();
        var topSilver = await holders
            .OrderByDescending(c => (long)c.Silver + (long)c.StoredSilver).ThenBy(c => c.CharacterID)
            .Take(topCount)
            .Select(c => new TopHolderEntry(c.CharacterID, c.Name, c.Level, c.Rebirths, c.CurrentClass,
                c.Silver, c.StoredSilver, (ulong)c.Silver + c.StoredSilver, c.Jewels))
            .ToArrayAsync(ct);
        var topCp = await holders
            .Where(c => c.Jewels > 0)
            .OrderByDescending(c => c.Jewels).ThenBy(c => c.CharacterID)
            .Take(topCount)
            .Select(c => new TopHolderEntry(c.CharacterID, c.Name, c.Level, c.Rebirths, c.CurrentClass,
                c.Silver, c.StoredSilver, (ulong)c.Silver + c.StoredSilver, c.Jewels))
            .ToArrayAsync(ct);

        return (totals, topSilver, topCp);
    }

    private static async Task<EconomyTrends> QueryTrendsAsync(
        IMetricRanges metrics, ObservabilityOptions options, DateTime startUtc, DateTime endUtc, TimeSpan step, CancellationToken ct)
    {
        try
        {
            var silver = metrics.PrometheusAsync(options.ScopeMetric("fetih_economy_silver_total"), "", startUtc, endUtc, step, ct);
            var cp = metrics.PrometheusAsync(options.ScopeMetric("fetih_economy_cp_total"), "", startUtc, endUtc, step, ct);
            return new EconomyTrends(true, (await silver).GetValueOrDefault("") ?? [], (await cp).GetValueOrDefault("") ?? []);
        }
        catch (MetricsUnavailableException)
        {
            return new EconomyTrends(false, [], []);
        }
    }

    private static async Task<MoneyFlowSummary> QueryFlowAsync(
        IMetricRanges metrics, DateTime startUtc, DateTime endUtc, TimeSpan step, CancellationToken ct)
    {
        try
        {
            var shop = metrics.LokiAsync(EconomyFlowAggregator.Query(EconomyFlowAggregator.ShopQuery, step),
                "event", startUtc, endUtc, step, ct);
            var pickups = metrics.LokiAsync(EconomyFlowAggregator.Query(EconomyFlowAggregator.PickupQuery, step),
                "", startUtc, endUtc, step, ct);
            var byEvent = await shop;
            return EconomyFlowAggregator.FromSeries(
                byEvent.GetValueOrDefault("item.bought") ?? [],
                byEvent.GetValueOrDefault("item.sold") ?? [],
                (await pickups).GetValueOrDefault("") ?? []);
        }
        catch (MetricsUnavailableException)
        {
            return new MoneyFlowSummary(false, 0, 0, 0, 0, null, false, []);
        }
    }
}

namespace Fetih.Web.Features.Economy;

using Fetih.Web.Features.Logs;

// Silver moving between players and the game: spent at NPC shops (item.bought), earned selling
// to them (item.sold) and picked up off the ground (item.picked_up, monster drops and silver
// players dropped), summed per bucket by Loki from the log lines' price and silver fields
public static class EconomyFlowAggregator
{
    public const string ShopQuery =
        "sum by (event) (sum_over_time({server=\"game\", event=~\"item\\\\.bought|item\\\\.sold\"} | json price | unwrap price [STEP]))";
    public const string PickupQuery =
        "sum(sum_over_time({server=\"game\", event=\"item.picked_up\"} | json silver | unwrap silver [STEP]))";
    public const double SpikeMultiplier = 4.0;
    public const long SpikeMinSilver = 100_000;

    public static string Query(string template, TimeSpan step) =>
        template.Replace("STEP", $"{(int)step.TotalSeconds}s");

    // Each bucket is the step that ends at its timestamp
    public static MoneyFlowSummary FromSeries(TrendPoint[] spent, TrendPoint[] earned, TrendPoint[] picked)
    {
        var times = spent.Concat(earned).Concat(picked).Select(p => p.Timestamp).Distinct().Order().ToArray();
        var spentAt = spent.ToDictionary(p => p.Timestamp, p => (long)p.Value);
        var earnedAt = earned.ToDictionary(p => p.Timestamp, p => (long)p.Value);
        var pickedAt = picked.ToDictionary(p => p.Timestamp, p => (long)p.Value);

        var buckets = times.Select(t =>
        {
            long s = spentAt.GetValueOrDefault(t), e = earnedAt.GetValueOrDefault(t), p = pickedAt.GetValueOrDefault(t);
            return new MoneyFlowBucket(t, s, e, p, e + p - s);
        }).ToArray();

        long totalSpent = buckets.Sum(b => b.SpentAtNpcs);
        long totalEarned = buckets.Sum(b => b.EarnedFromNpcs);
        long totalPicked = buckets.Sum(b => b.PickedUpSilver);
        var (spikeRatio, spikeDetected) = DetectSpike(buckets);
        return new MoneyFlowSummary(true, totalSpent, totalEarned, totalPicked, totalEarned + totalPicked - totalSpent,
            spikeRatio, spikeDetected, buckets);
    }

    private static (double? Ratio, bool Detected) DetectSpike(MoneyFlowBucket[] buckets)
    {
        var nonZeroInflows = buckets.Select(b => b.EarnedFromNpcs + b.PickedUpSilver).Where(v => v > 0).ToArray();
        if (nonZeroInflows.Length < 3)
            return (null, false);

        long maxInflow = nonZeroInflows.Max();
        double avgOthers = nonZeroInflows.Where(v => v < maxInflow).DefaultIfEmpty(0).Average();
        if (avgOthers <= 0)
            return (null, false);

        double ratio = Math.Round(maxInflow / avgOthers, 2);
        bool detected = maxInflow >= SpikeMinSilver && ratio >= SpikeMultiplier;
        return (ratio, detected);
    }
}

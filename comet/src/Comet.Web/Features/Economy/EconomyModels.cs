namespace Comet.Web.Features.Economy;

public sealed record EconomyTotals(
    ulong TotalSilver,
    ulong PocketSilver,
    ulong StoredSilver,
    ulong TotalConquerPoints,
    ulong GuildSilverFund,
    ulong GuildConquerPointsFund,
    int CharacterCount);

public sealed record TopHolderEntry(
    uint CharacterId,
    string Name,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    ulong PocketSilver,
    ulong StoredSilver,
    ulong TotalSilver,
    uint ConquerPoints);

public sealed record TrendPoint(DateTime Timestamp, double Value);

public sealed record EconomyTrends(
    bool PrometheusAvailable,
    TrendPoint[] Silver,
    TrendPoint[] ConquerPoints);

public sealed record MoneyFlowBucket(
    DateTime Timestamp,
    long SpentAtNpcs,
    long EarnedFromNpcs,
    long PickedUpSilver,
    long NetFlow);

public sealed record MoneyFlowSummary(
    bool LokiAvailable,
    long TotalSpentAtNpcs,
    long TotalEarnedFromNpcs,
    long TotalPickedUpSilver,
    long NetFlow,
    double? SpikeRatio,
    bool SpikeDetected,
    MoneyFlowBucket[] Buckets);

public sealed record EconomyResponse(
    DateTime GeneratedAt,
    string Range,
    EconomyTotals Totals,
    TopHolderEntry[] TopSilverHolders,
    TopHolderEntry[] TopCpHolders,
    EconomyTrends Trends,
    MoneyFlowSummary Flow);

namespace Comet.Web.Features.PublicHeatmap;

public sealed record HeatmapMapEntry(
    uint MapId,
    string Name,
    string? Minimap,
    int Players,
    double Share);

public sealed record PublicHeatmapResponse(
    bool GameOnline,
    int TotalOnline,
    int MinPlayers,
    int CacheSeconds,
    DateTime CachedAt,
    HeatmapMapEntry[] Maps);

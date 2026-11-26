namespace Fetih.Web.Features.PublicHeatmap;

using Fetih.Web.Features.Catalogs;

public sealed class PublicHeatmapService
{
    public const int DefaultMinPlayers = 3;
    public const int DefaultCacheSeconds = 60;

    private readonly IHeatmapGameSource game;
    private readonly CatalogStore catalogs;
    private readonly TimeProvider time;
    private readonly int minPlayers;
    private readonly TimeSpan cacheDuration;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private PublicHeatmapResponse? cached;
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    public PublicHeatmapService(
        IHeatmapGameSource game,
        CatalogStore catalogs,
        IConfiguration configuration,
        TimeProvider? time = null)
        : this(
            game,
            catalogs,
            ReadInt(configuration, "Web:HeatmapMinPlayers", DefaultMinPlayers, min: 1),
            TimeSpan.FromSeconds(ReadInt(configuration, "Web:HeatmapCacheSeconds", DefaultCacheSeconds, min: 1)),
            time)
    {
    }

    internal PublicHeatmapService(
        IHeatmapGameSource game,
        CatalogStore catalogs,
        int minPlayers,
        TimeSpan cacheDuration,
        TimeProvider? time = null)
    {
        this.game = game;
        this.catalogs = catalogs;
        this.minPlayers = Math.Max(minPlayers, 1);
        this.cacheDuration = cacheDuration < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : cacheDuration;
        this.time = time ?? TimeProvider.System;
    }

    public async Task<PublicHeatmapResponse> GetAsync(CancellationToken ct = default)
    {
        var now = this.time.GetUtcNow();
        if (this.cached is { } hit && now < this.expiresAt)
            return hit;

        await this.refreshLock.WaitAsync(ct);
        try
        {
            now = this.time.GetUtcNow();
            if (this.cached is { } secondHit && now < this.expiresAt)
                return secondHit;

            var fresh = await this.FetchAsync(now);
            this.cached = fresh;
            this.expiresAt = now + this.cacheDuration;
            return fresh;
        }
        finally
        {
            this.refreshLock.Release();
        }
    }

    private async Task<PublicHeatmapResponse> FetchAsync(DateTimeOffset now)
    {
        int cacheSeconds = (int)Math.Ceiling(this.cacheDuration.TotalSeconds);
        if (!this.game.Connected)
            return new PublicHeatmapResponse(false, 0, this.minPlayers, cacheSeconds, now.UtcDateTime, []);

        try
        {
            var players = await this.game.OnlinePlayersAsync();
            int total = players.Count;

            var maps = players
                .GroupBy(p => p.MapID)
                .Select(g => (MapId: g.Key, Count: g.Count()))
                .Where(x => x.Count >= this.minPlayers)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.MapId)
                .Select(x =>
                {
                    string name = this.catalogs.MapDisplayName(x.MapId) ?? $"Map {x.MapId}";
                    string? minimap = this.catalogs.MapsById.TryGetValue(x.MapId, out var entry) ? entry.Minimap : null;
                    double share = total > 0 ? Math.Round((double)x.Count / total, 4) : 0;
                    return new HeatmapMapEntry(x.MapId, name, minimap, x.Count, share);
                })
                .ToArray();

            return new PublicHeatmapResponse(true, total, this.minPlayers, cacheSeconds, now.UtcDateTime, maps);
        }
        catch
        {
            return new PublicHeatmapResponse(false, 0, this.minPlayers, cacheSeconds, now.UtcDateTime, []);
        }
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int min) =>
        int.TryParse(configuration[key], out int value) && value >= min ? value : fallback;
}

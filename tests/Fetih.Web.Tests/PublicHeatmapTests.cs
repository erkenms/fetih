#nullable enable
namespace Fetih.Web.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.PublicHeatmap;
using Xunit;

[Collection(WebCollection.Name)]
public class PublicHeatmapTests(WebFixture fixture)
{
    [Fact]
    public async Task AnonymousVisitorCanFetchHeatmapWhenGameIsOffline()
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/public/heatmap");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(doc.GetProperty("gameOnline").GetBoolean());
        Assert.Equal(0, doc.GetProperty("totalOnline").GetInt32());
        Assert.Equal(PublicHeatmapService.DefaultMinPlayers, doc.GetProperty("minPlayers").GetInt32());
        Assert.Equal(PublicHeatmapService.DefaultCacheSeconds, doc.GetProperty("cacheSeconds").GetInt32());
        Assert.Empty(doc.GetProperty("maps").EnumerateArray());
    }

    [Fact]
    public async Task HidesMapsBelowThresholdAndExposesOnlyAggregates()
    {
        var source = new FakeHeatmapGameSource
        {
            Connected = true,
            Players =
            [
                Player(1, "Alice", 1002, 430, 380),
                Player(2, "Bob", 1002, 435, 382),
                Player(3, "Cara", 1002, 440, 385),
                Player(4, "Dan", 1002, 445, 390),
                Player(5, "Eve", 1011, 200, 260),
                Player(6, "Frank", 1011, 210, 265),
                Player(7, "Grace", 1011, 220, 270),
                // Below threshold of 3: must not appear
                Player(8, "Heidi", 1000, 500, 650),
                Player(9, "Ivan", 1000, 505, 655),
                Player(10, "Judy", 1015, 717, 577),
            ],
        };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var service = new PublicHeatmapService(source, new CatalogStore(null, null, null), minPlayers: 3, TimeSpan.FromSeconds(60), clock);

        var result = await service.GetAsync();
        Assert.True(result.GameOnline);
        Assert.Equal(10, result.TotalOnline);
        Assert.Equal(3, result.MinPlayers);
        Assert.Equal(2, result.Maps.Length);

        Assert.Equal(1002u, result.Maps[0].MapId);
        Assert.Equal(4, result.Maps[0].Players);
        Assert.Equal(0.4, result.Maps[0].Share);

        Assert.Equal(1011u, result.Maps[1].MapId);
        Assert.Equal(3, result.Maps[1].Players);
        Assert.Equal(0.3, result.Maps[1].Share);

        // Serialized payload must not contain player names or coordinates
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Alice", json);
        Assert.DoesNotContain("Heidi", json);
        Assert.DoesNotContain("430", json);
    }

    [Fact]
    public async Task CachesResultsForConfiguredWindowThenRefreshes()
    {
        var source = new FakeHeatmapGameSource
        {
            Connected = true,
            Players =
            [
                Player(1, "A", 1002, 400, 400),
                Player(2, "B", 1002, 401, 401),
            ],
        };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var service = new PublicHeatmapService(source, new CatalogStore(null, null, null), minPlayers: 2, TimeSpan.FromSeconds(60), clock);

        var first = await service.GetAsync();
        Assert.Equal(1, source.Calls);
        Assert.Single(first.Maps);
        Assert.Equal(2, first.Maps[0].Players);

        // New player logs in; within the 60s window the cached result is returned
        source.Players.Add(Player(3, "C", 1002, 402, 402));
        clock.Advance(TimeSpan.FromSeconds(59));
        var cached = await service.GetAsync();
        Assert.Equal(1, source.Calls);
        Assert.Equal(2, cached.Maps[0].Players);

        // Once 60s have passed, the service queries the game again and picks up the new count
        clock.Advance(TimeSpan.FromSeconds(1));
        var refreshed = await service.GetAsync();
        Assert.Equal(2, source.Calls);
        Assert.Equal(3, refreshed.Maps[0].Players);
        Assert.Equal(3, refreshed.TotalOnline);
    }

    [Fact]
    public async Task HandlesRpcFailureAsOffline()
    {
        var source = new FakeHeatmapGameSource { Connected = true, ThrowOnCall = true };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var service = new PublicHeatmapService(source, new CatalogStore(null, null, null), minPlayers: 3, TimeSpan.FromSeconds(60), clock);

        var result = await service.GetAsync();
        Assert.False(result.GameOnline);
        Assert.Equal(0, result.TotalOnline);
        Assert.Empty(result.Maps);
    }

    private static OnlinePlayer Player(uint id, string name, uint mapId, ushort x, ushort y) => new()
    {
        CharacterID = id,
        AccountID = id,
        Name = name,
        Level = 50,
        Class = 10,
        MapID = mapId,
        X = x,
        Y = y,
    };

    private sealed class FakeHeatmapGameSource : IHeatmapGameSource
    {
        public bool Connected { get; set; }
        public bool ThrowOnCall { get; set; }
        public int Calls { get; private set; }
        public List<OnlinePlayer> Players { get; set; } = [];

        public Task<List<OnlinePlayer>> OnlinePlayersAsync()
        {
            this.Calls++;
            if (this.ThrowOnCall)
                throw new InvalidOperationException("RPC disconnected");
            return Task.FromResult(new List<OnlinePlayer>(this.Players));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset current = start;
        public override DateTimeOffset GetUtcNow() => this.current;
        public void Advance(TimeSpan delta) => this.current += delta;
    }
}

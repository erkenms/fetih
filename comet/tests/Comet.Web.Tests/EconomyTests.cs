#nullable enable
namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Web.Features.Economy;
using Comet.Web.Features.Logs;
using Xunit;

[Collection(WebCollection.Name)]
public class EconomyTests(WebFixture fixture)
{
    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/economy")).StatusCode);
    }

    [Fact]
    public async Task ModeratorReceivesEconomyTotalsAndHolders()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");
        var response = await client.GetAsync("/api/economy?range=24h");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("24h", doc.GetProperty("range").GetString());
        Assert.True(doc.GetProperty("totals").TryGetProperty("totalSilver", out _));
        Assert.True(doc.GetProperty("totals").TryGetProperty("totalConquerPoints", out _));
        Assert.True(doc.GetProperty("topSilverHolders").ValueKind == JsonValueKind.Array);
        Assert.True(doc.GetProperty("topCpHolders").ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public void FlowReadsLokisMatrixPerEventAndFlagsASpike()
    {
        // Loki's answer to ShopQuery: one series per event, [unix seconds, "sum"] per step
        const string shop = """
        {"status":"success","data":{"resultType":"matrix","result":[
          {"metric":{"event":"item.bought"},"values":[[1764032400,"15000"]]},
          {"metric":{"event":"item.sold"},"values":[[1764032400,"5000"],[1764043200,"500000"]]}
        ]}}
        """;
        const string pickups = """
        {"status":"success","data":{"resultType":"matrix","result":[
          {"metric":{},"values":[[1764032400,"12000"],[1764036000,"10000"],[1764039600,"11000"]]}
        ]}}
        """;

        var byEvent = MetricRanges.ParseMatrix(shop, "event");
        var picked = MetricRanges.ParseMatrix(pickups, "");
        var summary = EconomyFlowAggregator.FromSeries(byEvent["item.bought"], byEvent["item.sold"], picked[""]);

        Assert.Equal(15_000, summary.TotalSpentAtNpcs);
        Assert.Equal(505_000, summary.TotalEarnedFromNpcs);
        Assert.Equal(33_000, summary.TotalPickedUpSilver);
        Assert.Equal(523_000, summary.NetFlow);
        Assert.Equal(
            [new DateTime(2025, 11, 25, 1, 0, 0, DateTimeKind.Utc), new(2025, 11, 25, 2, 0, 0, DateTimeKind.Utc),
             new(2025, 11, 25, 3, 0, 0, DateTimeKind.Utc), new(2025, 11, 25, 4, 0, 0, DateTimeKind.Utc)],
            summary.Buckets.Select(b => b.Timestamp));
        Assert.Equal(2_000, summary.Buckets[0].NetFlow);
        Assert.True(summary.SpikeDetected);
    }

    [Fact]
    public void FlowQueriesSumThePriceAndSilverFieldsTheGameLogs()
    {
        string shop = EconomyFlowAggregator.Query(EconomyFlowAggregator.ShopQuery, TimeSpan.FromHours(1));
        Assert.Equal(
            "sum by (event) (sum_over_time({server=\"game\", event=~\"item\\\\.bought|item\\\\.sold\"} | json price | unwrap price [3600s]))",
            shop);
        Assert.Contains("| json silver | unwrap silver [3600s]",
            EconomyFlowAggregator.Query(EconomyFlowAggregator.PickupQuery, TimeSpan.FromHours(1)));
    }
}

namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Comet.Web.Features.Logs;
using Comet.Web.Features.Dashboard;
using Xunit;

[Collection(WebCollection.Name)]
public class LogsTests(WebFixture fixture)
{
    [Fact]
    public void LogQlHidesPacketsByDefaultAndIncludesWhenRequested()
    {
        var defaultQuery = LogQl.BuildQuery(new LogQueryFilter());
        Assert.Equal("{server=~\".+\", event!~\"packet\\\\.(in|out)\"}", defaultQuery);

        var withPackets = LogQl.BuildQuery(new LogQueryFilter(IncludePackets: true));
        Assert.Equal("{server=~\".+\"}", withPackets);
    }

    [Fact]
    public void LogQlFiltersCharacterAccountServerLevelPrefixAndSearch()
    {
        var query = LogQl.BuildQuery(new LogQueryFilter(
            CharacterId: 1000042,
            AccountId: 7,
            Server: "game",
            Level: "warn",
            EventPrefix: "item.",
            Search: "dropped \"sword\""));

        Assert.Equal(
            "{server=\"game\", level=\"warn\", event=~\"^item\\\\..*\"} | json | characterId=\"1000042\" | accountId=\"7\" |= \"dropped \\\"sword\\\"\"",
            query);
    }

    [Fact]
    public void GrafanaUrlsTargetPlayerDashboardOrOverview()
    {
        var from = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        string playerUrl = LogQl.BuildGrafanaUrl("http://localhost:3000/",
            new LogQueryFilter(CharacterId: 1000001, AccountId: 5), from, to);
        Assert.StartsWith("http://localhost:3000/d/comet-player/player?", playerUrl);
        Assert.Contains("var-characterId=1000001", playerUrl);
        Assert.Contains("var-accountId=5", playerUrl);

        string overviewUrl = LogQl.BuildGrafanaUrl("http://localhost:3000",
            new LogQueryFilter(), from, to);
        Assert.StartsWith("http://localhost:3000/d/comet-overview/overview?", overviewUrl);

        string exploreUrl = LogQl.BuildGrafanaExploreUrl("http://localhost:3000", "{server=~\".+\"}", from, to);
        Assert.StartsWith("http://localhost:3000/explore?schemaVersion=1&panes=", exploreUrl);
    }

    [Fact]
    public void LokiClientParsesMultiStreamJsonAndSortsDescending()
    {
        string json = """
            {
              "status": "success",
              "data": {
                "resultType": "streams",
                "result": [
                  {
                    "stream": { "server": "game", "level": "info", "event": "item.bought" },
                    "values": [
                      [ "1763980000000000000", "{\"v\":1,\"ts\":\"2026-11-24T10:00:00.000Z\",\"level\":\"info\",\"server\":\"game\",\"event\":\"item.bought\",\"msg\":\"Bought Sword\",\"characterId\":1000001,\"accountId\":2,\"price\":500}" ]
                    ]
                  },
                  {
                    "stream": { "server": "game", "level": "warn", "event": "pk.jailed" },
                    "values": [
                      [ "1763980600000000000", "{\"v\":1,\"ts\":\"2026-11-24T10:10:00.000Z\",\"level\":\"warn\",\"server\":\"game\",\"event\":\"pk.jailed\",\"msg\":\"Jailed player\",\"characterId\":1000001,\"src\":\"Comet.Game.Pk\",\"exc\":\"System.Exception: test\"}" ]
                    ]
                  }
                ]
              }
            }
            """;

        var entries = LokiClient.ParseResponse(json);
        Assert.Equal(2, entries.Length);

        Assert.Equal("pk.jailed", entries[0].Event);
        Assert.Equal("warn", entries[0].Level);
        Assert.Equal(1000001u, entries[0].CharacterId);
        Assert.Null(entries[0].AccountId);
        Assert.Equal("Comet.Game.Pk", entries[0].Source);
        Assert.Equal("System.Exception: test", entries[0].Exception);

        Assert.Equal("item.bought", entries[1].Event);
        Assert.Equal(2u, entries[1].AccountId);
        Assert.Equal("500", entries[1].Fields["price"]);
        Assert.True(entries[0].Timestamp > entries[1].Timestamp);
    }

    [Fact]
    public async Task LokiClientThrowsOnErrorResponse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("bad gateway", Encoding.UTF8, "text/plain"),
        });
        var client = new LokiClient(new HttpClient(handler), new ObservabilityOptions { LokiUrl = "http://127.0.0.1:3100" });

        await Assert.ThrowsAsync<LokiUnavailableException>(() =>
            client.QueryRangeAsync("{server=~\".+\"}", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 50, CancellationToken.None));
    }

    [Fact]
    public async Task TimelineRequiresViewLogsPermission()
    {
        var anon = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/logs/timeline")).StatusCode);

        var player = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/logs/timeline")).StatusCode);

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var moderator = await fixture.LoginAsync(modEmail, "secret1");

        var badRange = await moderator.GetAsync("/api/logs/timeline?from=2026-11-24T12:00:00Z&to=2026-11-24T10:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);

        var response = await moderator.GetAsync("/api/logs/characters/1000001");
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Expected 200 or 503, got {response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("characterId=\"1000001\"", body.GetProperty("query").GetString());
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

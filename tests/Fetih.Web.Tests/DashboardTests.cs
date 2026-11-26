namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fetih.Web.Features.Dashboard;
using Xunit;

[Collection(WebCollection.Name)]
public class DashboardTests(WebFixture fixture)
{
    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task ModeratorSeesCountsWithTheGameOffline()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");
        var body = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.Equal(JsonValueKind.Null, body.GetProperty("game").ValueKind);
        Assert.Empty(body.GetProperty("onlineAccounts").EnumerateArray());
        Assert.True(body.GetProperty("counts").GetProperty("accounts").GetInt32() >= 2);
        Assert.True(body.GetProperty("counts").GetProperty("accountsLastDay").GetInt32() >= 1);
        Assert.Equal("/grafana", body.GetProperty("grafanaUrl").GetString());
    }

    [Fact]
    public void CountsAreSummedPerLabel()
    {
        using var doc = JsonDocument.Parse("""
            {"status":"success","data":{"resultType":"vector","result":[
              {"metric":{"level":"error"},"value":[1795551763.2,"3"]},
              {"metric":{"level":"warn"},"value":[1795551763.2,"12"]},
              {"metric":{"level":"fatal"},"value":[1795551763.2,"1"]}]}}
            """);
        var counts = LogStats.ParseCounts(doc, "level");
        Assert.Equal(3, counts["error"]);
        Assert.Equal(12, counts["warn"]);
        Assert.Equal(1, counts["fatal"]);

        using var total = JsonDocument.Parse("""
            {"data":{"result":[{"metric":{},"value":[1795551763.2,"7"]}]}}
            """);
        Assert.Equal(7, LogStats.ParseCounts(total, "")[""]);
    }

    [Fact]
    public void LoginLinesCarryTheCharacter()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"result":[{"stream":{"event":"session.login","level":"info","server":"game"},"values":[
              ["1795514607848000000","{\"msg\":\"Salih logged in\",\"name\":\"Salih\",\"accountId\":1,\"characterId\":1000000,\"_level\":100,\"ip\":\"10.0.0.2\",\"mapId\":1002}"],
              ["1795514700000000000","{\"msg\":\"Other logged in\",\"name\":\"Other\",\"accountId\":2,\"characterId\":1000001,\"_level\":15,\"ip\":\"10.0.0.3\",\"mapId\":1036}"]]}]}}
            """);
        var logins = LogStats.ParseLogins(doc).ToList();
        Assert.Equal(["Other", "Salih"], logins.Select(l => l.Name));
        var salih = logins[1];
        Assert.Equal(1u, salih.AccountId);
        Assert.Equal(1000000u, salih.CharacterId);
        Assert.Equal(100, salih.Level);
        Assert.Equal("10.0.0.2", salih.IpAddress);
        Assert.Equal(1002u, salih.MapId);
        Assert.Equal(new DateTime(2026, 11, 24, 10, 3, 27, 848, DateTimeKind.Utc), salih.Timestamp);
    }

    [Fact]
    public void ErrorLinesShowTheirMessage()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"result":[{"stream":{"event":"map.load_failed","level":"error","server":"game"},"values":[
              ["1795514607848000000","{\"msg\":\"Couldn't load map 1067\"}"],
              ["1795514607000000000","not json"]]}]}}
            """);
        var lines = LogStats.ParseLines(doc).ToList();
        Assert.Equal("Couldn't load map 1067", lines[0].Message);
        Assert.Equal("map.load_failed", lines[0].Event);
        Assert.Equal("error", lines[0].Level);
        Assert.Equal("not json", lines[1].Message);
    }
}

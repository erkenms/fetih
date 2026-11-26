namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fetih.Core.Logging;
using Fetih.Web.Features.Logs;
using Fetih.Web.Features.Pk;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class PkTests(WebFixture fixture)
{
    private async Task ExecuteAsync(string sql)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("FETIH_TEST_DB_HOST") is { Length: > 0 } host ? host : "localhost",
            UserID = Environment.GetEnvironmentVariable("FETIH_TEST_DB_USER") is { Length: > 0 } user ? user : "root",
            Password = Environment.GetEnvironmentVariable("FETIH_TEST_DB_PASSWORD") is { Length: > 0 } pw ? pw : "password",
            Database = fixture.GameSchema,
        };
        await using var db = new MySqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        await new MySqlCommand(sql, db).ExecuteNonQueryAsync();
    }

    private async Task<HttpClient> ModeratorAsync() =>
        await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

    [Fact]
    public void JailTimesReadTheGameLogFields()
    {
        var newer = new DateTime(2026, 11, 20, 13, 0, 0, DateTimeKind.Utc);
        var older = newer.AddHours(-4);
        LogEntry Entry(DateTime ts, string evt, uint characterId, params (string Key, string Value)[] fields) =>
            new(ts, "game", "info", evt, "", characterId, null, "", null, fields.ToDictionary(f => f.Key, f => f.Value));

        var times = PkFeature.JailTimes(
        [
            Entry(newer, LogEvents.PkJailed, 1000001, ("name", "Alice"), ("reason", "killed_by_guard")),
            Entry(newer, LogEvents.GmJail, 1000002, ("result", "NotFound")),
            Entry(older, LogEvents.GmJail, 1000002, ("result", "offline")),
            Entry(older, LogEvents.PkJailed, 1000001, ("reason", "gm")),
            Entry(older, LogEvents.PkJailed, 1000003, ("reason", "gm")),
        ], [1000001, 1000002]);

        Assert.Equal(2, times.Count);
        Assert.Equal((newer, "killed_by_guard"), times[1000001]);
        Assert.Equal((older, "admin"), times[1000002]);
    }

    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/pk/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/pk/jail")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/pk/active")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/pk/leaderboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/pk/detained")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/pk/jail/1/release", null)).StatusCode);
    }

    [Fact]
    public async Task JailListsInmatesAndReleaseChecksGameRpc()
    {
        await ExecuteAsync("""
            INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass, MapID, X, Y, KillPoints) VALUES
              (8100001, 8100001, 'PkBlackInmate', 120, 15, 6000, 29, 72, 135),
              (8100002, 8100002, 'PkRedInmate', 95, 25, 6000, 31, 70, 45),
              (8100003, 8100003, 'PkFreePlayer', 110, 45, 1002, 430, 380, 110);
            """);

        var client = await ModeratorAsync();
        var jail = await client.GetFromJsonAsync<JsonElement>("/api/pk/jail?search=Inmate");
        Assert.False(jail.GetProperty("gameConnected").GetBoolean());
        var inmates = jail.GetProperty("inmates").EnumerateArray().ToList();
        Assert.Equal(2, inmates.Count);

        var black = inmates[0];
        Assert.Equal("PkBlackInmate", black.GetProperty("character").GetProperty("name").GetString());
        Assert.Equal(135, black.GetProperty("pkPoints").GetInt32());
        Assert.Equal("black", black.GetProperty("nameColor").GetString());
        Assert.Equal(500000, black.GetProperty("bailRequired").GetInt32());

        var red = inmates[1];
        Assert.Equal("PkRedInmate", red.GetProperty("character").GetProperty("name").GetString());
        Assert.Equal("red", red.GetProperty("nameColor").GetString());
        Assert.Equal(0, red.GetProperty("bailRequired").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/pk/jail/8199999/release", null)).StatusCode);
        // Game is offline in WebFixture, so releasing an existing character reaches the RPC and returns 503
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/pk/jail/8100001/release", null)).StatusCode);
    }

    [Fact]
    public async Task LeaderboardOrdersByPkPointsAndFiltersByColor()
    {
        await ExecuteAsync("""
            INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass, MapID, KillPoints) VALUES
              (8200001, 8200001, 'PkBoardZero', 100, 15, 1002, 0),
              (8200002, 8200002, 'PkBoardWhite', 100, 15, 1002, 15),
              (8200003, 8200003, 'PkBoardRed', 100, 15, 1002, 60),
              (8200004, 8200004, 'PkBoardBlack', 100, 15, 6000, 220);
            """);

        var client = await ModeratorAsync();
        var all = await client.GetFromJsonAsync<JsonElement>("/api/pk/leaderboard?search=PkBoard");
        var names = all.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("character").GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(["PkBoardBlack", "PkBoardRed", "PkBoardWhite"], names);

        var onlyBlack = await client.GetFromJsonAsync<JsonElement>("/api/pk/leaderboard?search=PkBoard&color=black");
        var blackEntry = Assert.Single(onlyBlack.GetProperty("entries").EnumerateArray());
        Assert.Equal("PkBoardBlack", blackEntry.GetProperty("character").GetProperty("name").GetString());
        Assert.True(blackEntry.GetProperty("inJail").GetBoolean());

        var onlyRed = await client.GetFromJsonAsync<JsonElement>("/api/pk/leaderboard?search=PkBoard&color=red");
        var redEntry = Assert.Single(onlyRed.GetProperty("entries").EnumerateArray());
        Assert.Equal("PkBoardRed", redEntry.GetProperty("character").GetProperty("name").GetString());
    }

    [Fact]
    public async Task DetainedItemsReportOwnerKillerInstanceAndStatus()
    {
        var recent = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");
        var expired = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd HH:mm:ss");
        await ExecuteAsync($"""
            INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass) VALUES
              (8300001, 8300001, 'PkVictim', 115, 15),
              (8300002, 8300002, 'PkHunter', 125, 45);
            INSERT INTO item (ItemID, CharacterID, TypeID, Position, Plus, Bless, Gem1, Gem2, Amount, AmountLimit) VALUES
              (8300101, 8300001, 410339, 250, 6, 3, 13, 0, 1000, 1000),
              (8300102, 8300001, 420339, 250, 4, 1, 0, 0, 1000, 1000);
            INSERT INTO detained_item (DetainID, ItemID, TargetID, TargetName, HunterID, HunterName, Cost, DetainedAt, Redeemed) VALUES
              (8301, 8300101, 8300001, 'PkVictim', 8300002, 'PkHunter', 500, '{recent}', 0),
              (8302, 8300102, 8300001, 'PkVictim', 8300002, 'PkHunter', 250, '{expired}', 0),
              (8303, 0,       8300001, 'PkVictim', 8300002, 'PkHunter', 150, '{recent}', 1);
            """);

        var client = await ModeratorAsync();
        var detained = await client.GetFromJsonAsync<JsonElement>("/api/pk/detained?search=PkVictim");
        var items = detained.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);

        var redeemable = items.Single(i => i.GetProperty("detainId").GetInt32() == 8301);
        Assert.Equal("redeemable", redeemable.GetProperty("status").GetString());
        Assert.Equal("PkVictim", redeemable.GetProperty("owner").GetProperty("name").GetString());
        Assert.Equal("PkHunter", redeemable.GetProperty("killer").GetProperty("name").GetString());
        Assert.Equal(500, redeemable.GetProperty("cost").GetInt32());
        Assert.Equal(6, redeemable.GetProperty("instance").GetProperty("plus").GetInt32());

        var expiredRow = items.Single(i => i.GetProperty("detainId").GetInt32() == 8302);
        Assert.Equal("expired", expiredRow.GetProperty("status").GetString());

        var redeemedRow = items.Single(i => i.GetProperty("detainId").GetInt32() == 8303);
        Assert.Equal("redeemed", redeemedRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, redeemedRow.GetProperty("instance").ValueKind);

        var onlyExpired = await client.GetFromJsonAsync<JsonElement>("/api/pk/detained?search=PkVictim&status=expired");
        Assert.Equal(8302, Assert.Single(onlyExpired.GetProperty("items").EnumerateArray()).GetProperty("detainId").GetInt32());

        var summary = await client.GetFromJsonAsync<JsonElement>("/api/pk/summary");
        Assert.True(summary.GetProperty("detainedRedeemable").GetInt32() >= 1);
        Assert.True(summary.GetProperty("detainedExpired").GetInt32() >= 1);
        Assert.True(summary.GetProperty("detainedRedeemed").GetInt32() >= 1);
    }
}

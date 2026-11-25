namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class SocialTests(WebFixture fixture)
{
    private async Task ExecuteAsync(string sql)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("COMET_TEST_DB_HOST") is { Length: > 0 } host ? host : "localhost",
            UserID = Environment.GetEnvironmentVariable("COMET_TEST_DB_USER") is { Length: > 0 } user ? user : "root",
            Password = Environment.GetEnvironmentVariable("COMET_TEST_DB_PASSWORD") is { Length: > 0 } pw ? pw : "password",
            Database = fixture.GameSchema,
        };
        await using var db = new MySqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        await new MySqlCommand(sql, db).ExecuteNonQueryAsync();
    }

    private async Task<HttpClient> ModeratorAsync() =>
        await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/social/guilds")).StatusCode);
    }

    [Fact]
    public async Task GuildShowsLeaderMembersAndAllies()
    {
        await ExecuteAsync("""
            INSERT INTO `character` (CharacterID, AccountID, Name, Level, CurrentClass) VALUES
              (7100001, 7100001, 'SocLeader', 120, 15), (7100002, 7100002, 'SocMember', 90, 25);
            INSERT INTO syndicate (SyndicateID, Name, LeaderID, Fund, Level, Created, Announcement)
              VALUES (7101, 'SocGuild', 7100001, 5000000, 2, 20261120, 'Hello'), (7102, 'SocAlly', 7100002, 100, 1, 20261121, '');
            INSERT INTO syndicate_member (CharacterID, SyndicateID, `Rank`, SilverDonation, Joined)
              VALUES (7100001, 7101, 1000, 300, 20261120), (7100002, 7101, 200, 50, 20261122);
            INSERT INTO syndicate_relation (SyndicateID, TargetID, Kind) VALUES (7101, 7102, 1);
            """);
        var client = await ModeratorAsync();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/social/guilds?search=SocGuild");
        var guild = Assert.Single(list.EnumerateArray());
        Assert.Equal("SocLeader", guild.GetProperty("leader").GetProperty("name").GetString());
        Assert.Equal(2, guild.GetProperty("members").GetInt32());
        Assert.Equal("2026-11-20", guild.GetProperty("created").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>("/api/social/guilds/7101");
        Assert.Equal("Hello", detail.GetProperty("announcement").GetString());
        var members = detail.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(["SocLeader", "SocMember"], members.Select(m => m.GetProperty("character").GetProperty("name").GetString()));
        Assert.Equal("SocAlly", Assert.Single(detail.GetProperty("allies").EnumerateArray()).GetProperty("name").GetString());
        Assert.Empty(detail.GetProperty("enemies").EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/social/guilds/999999")).StatusCode);
    }

    [Fact]
    public async Task NobilityRanksLikeTheGame()
    {
        await ExecuteAsync("""
            INSERT INTO `character` (CharacterID, AccountID, Name, Level) VALUES (7200001, 7200001, 'NobTop', 130);
            INSERT INTO nobility (CharacterID, Donation) VALUES (7200001, 9000000000000);
            """);
        var client = await ModeratorAsync();
        var board = await client.GetFromJsonAsync<JsonElement>("/api/social/nobility");
        var first = board.EnumerateArray().First();
        Assert.Equal(1, first.GetProperty("position").GetInt32());
        Assert.Equal("NobTop", first.GetProperty("character").GetProperty("name").GetString());
        Assert.Equal("King", first.GetProperty("rank").GetString());
    }

    [Fact]
    public async Task BoardListsPostsAndOnlyModeratorsDelete()
    {
        await ExecuteAsync("""
            INSERT INTO message_board (Channel, CharacterID, SyndicateID, AuthorName, Message, PostedAt)
              VALUES (2201, 7300001, 0, 'Seller', 'WTS gems', '2026-11-24 10:00:00');
            """);
        var client = await ModeratorAsync();
        var posts = await client.GetFromJsonAsync<JsonElement>("/api/social/board?channel=2201");
        var post = posts.EnumerateArray().Single(p => p.GetProperty("authorName").GetString() == "Seller");
        Assert.Equal("trade", post.GetProperty("channel").GetString());
        Assert.Equal("WTS gems", post.GetProperty("message").GetString());

        // The game is offline in tests, so a delete of an existing post reaches the RPC and fails there
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.DeleteAsync("/api/social/board/2201/7300001")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/social/board/2201/7399999")).StatusCode);
        var player = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.DeleteAsync("/api/social/board/2201/7300001")).StatusCode);
    }

    [Fact]
    public async Task GuildWarNeedsAGameMaster()
    {
        var moderator = await ModeratorAsync();
        var war = await moderator.GetFromJsonAsync<JsonElement>("/api/social/guildwar");
        Assert.Equal(JsonValueKind.Null, war.GetProperty("live").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.PostAsync("/api/social/guildwar/start", null)).StatusCode);

        var gm = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 3), "secret1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await gm.PostAsync("/api/social/guildwar/start", null)).StatusCode);
    }

    [Fact]
    public async Task ArenaAndFlowersAnswer()
    {
        await ExecuteAsync("""
            INSERT INTO `character` (CharacterID, AccountID, Name, Level) VALUES (7400001, 7400001, 'Fighter', 110);
            INSERT INTO arena (CharacterID, Points, WinsTotal) VALUES (7400001, 4321, 9);
            INSERT INTO flower (CharacterID, Lilies, LiliesToday) VALUES (7400001, 77, 3);
            """);
        var client = await ModeratorAsync();
        var arena = await client.GetFromJsonAsync<JsonElement>("/api/social/arena");
        Assert.Contains(arena.GetProperty("current").EnumerateArray(),
            e => e.GetProperty("character").GetProperty("name").GetString() == "Fighter" && e.GetProperty("points").GetUInt32() == 4321);
        var lilies = await client.GetFromJsonAsync<JsonElement>("/api/social/flowers?kind=Lily");
        var entry = lilies.EnumerateArray().Single(e => e.GetProperty("character").GetProperty("name").GetString() == "Fighter");
        Assert.Equal(77u, entry.GetProperty("amount").GetUInt32());
        Assert.Equal(3u, entry.GetProperty("today").GetUInt32());
    }
}

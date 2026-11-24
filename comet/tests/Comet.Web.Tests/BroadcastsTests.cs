namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Web.Features.Broadcasts;
using MySqlConnector;
using Xunit;

[Collection(WebCollection.Name)]
public class BroadcastsTests(WebFixture fixture)
{
    private static readonly string DbHost = Environment.GetEnvironmentVariable("COMET_TEST_DB_HOST") is { Length: > 0 } h ? h : "localhost";
    private static readonly string DbUser = Environment.GetEnvironmentVariable("COMET_TEST_DB_USER") is { Length: > 0 } u ? u : "root";
    private static readonly string DbPassword = Environment.GetEnvironmentVariable("COMET_TEST_DB_PASSWORD") is { Length: > 0 } p ? p : "password";

    [Fact]
    public async Task PlayerIsForbiddenFromBroadcasts()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/broadcasts/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/broadcasts/announcements")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/broadcasts/send",
            new { channel = "Center", text = "hello" })).StatusCode);
    }

    [Fact]
    public async Task ModeratorCanReadQueueAndAnnouncements()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.LoginAsync(email, "secret1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/broadcasts/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/broadcasts/announcements")).StatusCode);
    }

    [Fact]
    public async Task QueueReturnsOrderedItemsFromDatabase()
    {
        string cs = new MySqlConnectionStringBuilder
        {
            Server = DbHost, UserID = DbUser, Password = DbPassword, Database = fixture.GameSchema,
        }.ConnectionString;
        await using (var db = new MySqlConnection(cs))
        {
            await db.OpenAsync();
            await new MySqlCommand(
                "DELETE FROM broadcast; " +
                "INSERT INTO broadcast (ID, Position, CharacterID, SenderName, Message, Addition) VALUES " +
                "(11, 1, 1002, 'Second', 'Msg two', 0), " +
                "(10, 0, 1001, 'First', 'Msg one', 15);", db).ExecuteNonQueryAsync();
        }

        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.LoginAsync(email, "secret1");
        var queue = await client.GetFromJsonAsync<JsonElement>("/api/broadcasts/queue");
        Assert.False(queue.GetProperty("gameConnected").GetBoolean());
        var items = queue.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("First", items[0].GetProperty("senderName").GetString());
        Assert.Equal(15, items[0].GetProperty("addition").GetInt32());
        Assert.Equal("Second", items[1].GetProperty("senderName").GetString());
    }

    [Fact]
    public async Task SendBroadcastValidatesAndReportsOfflineGame()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.LoginAsync(email, "secret1");

        var empty = await client.PostAsJsonAsync("/api/broadcasts/send", new { channel = "Center", text = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("invalid_message", (await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var offline = await client.PostAsJsonAsync("/api/broadcasts/send", new { channel = "System", text = "Maintenance soon" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, offline.StatusCode);
        Assert.Equal("game_offline", (await offline.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnnouncementCrudAndAuditLifecycle()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.LoginAsync(email, "secret1");

        var badInterval = await client.PostAsJsonAsync("/api/broadcasts/announcements", new
        {
            title = "Test", channel = "Center", message = "Hello", intervalMinutes = 0, enabled = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, badInterval.StatusCode);

        var badWindow = await client.PostAsJsonAsync("/api/broadcasts/announcements", new
        {
            title = "Test",
            channel = "Center",
            message = "Hello",
            intervalMinutes = 10,
            enabled = true,
            startAt = "2026-11-25T12:00:00Z",
            endAt = "2026-11-25T11:00:00Z",
        });
        Assert.Equal(HttpStatusCode.BadRequest, badWindow.StatusCode);

        var createResp = await client.PostAsJsonAsync("/api/broadcasts/announcements", new
        {
            title = "Guild War",
            channel = "TopLeft",
            message = "Guild War starts at 20:00!",
            intervalMinutes = 30,
            enabled = true,
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        int id = created.GetProperty("id").GetInt32();
        Assert.Equal("TopLeft", created.GetProperty("channel").GetString());

        var list = await client.GetFromJsonAsync<JsonElement>("/api/broadcasts/announcements");
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);

        var updateResp = await client.PutAsJsonAsync($"/api/broadcasts/announcements/{id}", new
        {
            title = "Guild War Updated",
            channel = "System",
            message = "Guild War starts in 10 minutes!",
            intervalMinutes = 10,
            enabled = false,
        });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Guild War Updated", updated.GetProperty("title").GetString());
        Assert.False(updated.GetProperty("enabled").GetBoolean());

        var deleteResp = await client.DeleteAsync($"/api/broadcasts/announcements/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/audit?action=announcement.&targetId={id}");
        var actions = audit.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToList();
        Assert.Contains("announcement.create", actions);
        Assert.Contains("announcement.update", actions);
        Assert.Contains("announcement.delete", actions);
    }

    [Fact]
    public void AnnouncementSchedulerIsDueRules()
    {
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var baseRow = new DbWebAnnouncement
        {
            ID = 1,
            Title = "Tip",
            Channel = "Center",
            Message = "Welcome!",
            IntervalMinutes = 10,
            Enabled = true,
        };

        Assert.True(AnnouncementScheduler.IsDue(baseRow, now));

        baseRow.Enabled = false;
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));
        baseRow.Enabled = true;

        baseRow.StartAt = now.AddMinutes(5);
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));
        baseRow.StartAt = now.AddMinutes(-5);

        baseRow.EndAt = now;
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));
        baseRow.EndAt = now.AddMinutes(30);

        baseRow.LastSentAt = now.AddMinutes(-5);
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));

        baseRow.LastSentAt = now.AddMinutes(-10);
        Assert.True(AnnouncementScheduler.IsDue(baseRow, now));
    }
}

namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Web.Features.Logs;
using Comet.Web.Features.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class NotificationsTests(WebFixture fixture)
{
    private const string SampleWebhookUrl =
        "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz1234567890";

    [Fact]
    public void ValidatesAndMasksDiscordWebhookUrls()
    {
        Assert.True(DiscordWebhookSender.IsValidDiscordWebhookUrl(SampleWebhookUrl));
        Assert.True(DiscordWebhookSender.IsValidDiscordWebhookUrl(
            "https://discordapp.com/api/webhooks/123456789012345678/token_value_with_enough_length_1234"));
        Assert.False(DiscordWebhookSender.IsValidDiscordWebhookUrl("http://discord.com/api/webhooks/123456789012345678/abcdef1234567890123456"));
        Assert.False(DiscordWebhookSender.IsValidDiscordWebhookUrl("https://example.com/api/webhooks/123456789012345678/abcdef1234567890123456"));
        Assert.False(DiscordWebhookSender.IsValidDiscordWebhookUrl("not-a-url"));

        string masked = DiscordWebhookSender.MaskWebhookUrl(SampleWebhookUrl);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", masked);
        Assert.EndsWith("7890", masked);
        Assert.Contains("123456789012345678", masked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task NonAdministratorsAreForbidden(ushort authority)
    {
        string email = await fixture.CreateAccountAsync(authority);
        var client = await fixture.LoginAsync(email, "secret1");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/notifications/webhooks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/notifications/deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/notifications/webhooks", new
        {
            name = "Alerts",
            webhookUrl = SampleWebhookUrl,
            enabled = true,
            notifyBans = true,
            notifyErrors = true,
            notifyCheatReports = true,
            minIntervalSeconds = 0,
        })).StatusCode);
    }

    [Fact]
    public async Task AdminCanCrudWebhooksAndAuditMasksSecrets()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.LoginAsync(email, "secret1");

        var badUrl = await client.PostAsJsonAsync("/api/notifications/webhooks", new
        {
            name = "Bad",
            webhookUrl = "https://example.com/hook",
            enabled = true,
            notifyBans = true,
            notifyErrors = false,
            notifyCheatReports = false,
            minIntervalSeconds = 0,
        });
        Assert.Equal(HttpStatusCode.BadRequest, badUrl.StatusCode);
        Assert.Equal("invalid_webhook_url",
            (await badUrl.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var createResp = await client.PostAsJsonAsync("/api/notifications/webhooks", new
        {
            name = "Admin Alerts",
            webhookUrl = SampleWebhookUrl,
            enabled = true,
            notifyBans = true,
            notifyErrors = true,
            notifyCheatReports = true,
            minIntervalSeconds = 30,
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        int id = created.GetProperty("id").GetInt32();
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", created.GetProperty("maskedUrl").GetString());

        var list = await client.GetFromJsonAsync<JsonElement>("/api/notifications/webhooks");
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);

        var updateResp = await client.PutAsJsonAsync($"/api/notifications/webhooks/{id}", new
        {
            name = "Admin Alerts Updated",
            webhookUrl = "",
            enabled = false,
            notifyBans = true,
            notifyErrors = false,
            notifyCheatReports = true,
            minIntervalSeconds = 60,
        });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Admin Alerts Updated", updated.GetProperty("name").GetString());
        Assert.False(updated.GetProperty("enabled").GetBoolean());
        Assert.False(updated.GetProperty("notifyErrors").GetBoolean());

        var deleteResp = await client.DeleteAsync($"/api/notifications/webhooks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/audit?action=notification.&targetId={id}");
        var actions = audit.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("action").GetString())
            .ToList();
        Assert.Contains("notification.create", actions);
        Assert.Contains("notification.update", actions);
        Assert.Contains("notification.delete", actions);
    }

    [Fact]
    public async Task DispatcherSendsBansErrorsAndCheatReportsAndRespectsCooldown()
    {
        var dbFactory = fixture.Factory.Services.GetRequiredService<IDbContextFactory<NotificationsDbContext>>();

        var sent = new List<(string Url, DiscordEmbed Embed)>();
        var fakeLoki = new FakeLoki();
        var dispatcher = new NotificationDispatcher(dbFactory, fakeLoki, new FakeSender(sent));

        uint webhookId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var hook = new DbWebNotificationWebhook
            {
                Name = "Ops",
                WebhookUrl = SampleWebhookUrl,
                Enabled = true,
                NotifyBans = true,
                NotifyErrors = true,
                NotifyCheatReports = true,
                MinIntervalSeconds = 60,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Webhooks.Add(hook);
            await db.SaveChangesAsync();
            webhookId = hook.ID;
        }

        try
        {
            var t0 = DateTime.UtcNow;
            fakeLoki.Add(NotificationDispatcher.BanQuery, new LogEntry(
                t0.AddSeconds(4), "game", "info", "gm.ban",
                "Admin account 5 banned account 42 (botting): online", 1001, 42, "Remote", null, new()));
            fakeLoki.Add(NotificationDispatcher.BanCommandQuery, new LogEntry(
                t0.AddSeconds(5), "game", "info", "gm.command",
                "Salih[GM] ran /ban Bot1 botting: ok", 1000, 1, "Commands", null, new()));
            fakeLoki.Add(NotificationDispatcher.CheatQuery, new LogEntry(
                t0.AddSeconds(6), "game", "warn", "session.cheat_report",
                "Speedhack detected", 1001, 42, "Cheat", null, new()));
            fakeLoki.Add(Comet.Web.Features.Dashboard.LogStats.ErrorSelector, new LogEntry(
                t0.AddSeconds(7), "game", "error", "server.error",
                "Unhandled packet error", null, null, "Server", "System.Exception: boom", new()));

            // Bans share one category, so the cooldown lets the first of the two through
            int count = await dispatcher.PollOnceAsync(t0.AddSeconds(10) + NotificationDispatcher.IngestDelay);
            Assert.Equal(3, count);
            Assert.Equal(["Ban", "Cheat report", "Server error (game)"], sent.Select(x => x.Embed.Title));
            Assert.Contains("banned account 42", sent[0].Embed.Description);
            Assert.All(fakeLoki.Ends, end => Assert.Equal(t0.AddSeconds(10), end));

            sent.Clear();
            fakeLoki.Clear();
            fakeLoki.Add(Comet.Web.Features.Dashboard.LogStats.ErrorSelector, new LogEntry(
                t0.AddSeconds(20), "game", "error", "server.error",
                "Another error inside cooldown", null, null, "Server", null, new()));
            Assert.Equal(0, await dispatcher.PollOnceAsync(t0.AddSeconds(25) + NotificationDispatcher.IngestDelay));
            Assert.Empty(sent);
        }
        finally
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Webhooks.Where(x => x.ID == webhookId).ExecuteDeleteAsync();
            await db.Deliveries.Where(x => x.WebhookID == webhookId).ExecuteDeleteAsync();
        }
    }

    private sealed class FakeSender(List<(string Url, DiscordEmbed Embed)> sent) : IDiscordWebhookSender
    {
        public Task<WebhookSendResult> SendAsync(string webhookUrl, DiscordEmbed embed, CancellationToken ct)
        {
            sent.Add((webhookUrl, embed));
            return Task.FromResult(new WebhookSendResult(true, 204, null));
        }
    }

    private sealed class FakeLoki : ILokiClient
    {
        private readonly Dictionary<string, List<LogEntry>> byQuery = [];

        public List<DateTime> Ends { get; } = [];

        public void Add(string query, LogEntry entry)
        {
            if (!this.byQuery.TryGetValue(query, out var list))
                this.byQuery[query] = list = [];
            list.Add(entry);
        }

        public void Clear()
        {
            this.byQuery.Clear();
            this.Ends.Clear();
        }

        public Task<LogEntry[]> QueryRangeAsync(string logQl, DateTime startUtc, DateTime endUtc, int limit, CancellationToken ct)
        {
            this.Ends.Add(endUtc);
            return Task.FromResult(this.byQuery.TryGetValue(logQl, out var list)
                ? list.Where(e => e.Timestamp >= startUtc && e.Timestamp <= endUtc).ToArray()
                : []);
        }
    }
}

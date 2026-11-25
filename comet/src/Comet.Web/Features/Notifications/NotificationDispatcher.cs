namespace Comet.Web.Features.Notifications;

using System.Collections.Concurrent;
using System.Globalization;
using Comet.Core.Logging;
using Comet.Web.Features.Dashboard;
using Comet.Web.Features.Logs;
using Microsoft.EntityFrameworkCore;

public record NotificationEvent(
    string Category,
    string Title,
    string Summary,
    int Color,
    DateTime TimestampUtc,
    DiscordEmbedField[]? Fields = null);

public sealed class NotificationDispatcher(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    ILokiClient loki,
    IDiscordWebhookSender sender) : BackgroundService
{
    public const string CategoryBan = "ban";
    public const string CategoryError = "error";
    public const string CategoryCheatReport = "cheat_report";
    public const string CategoryTest = "test";
    public const int MaxDeliveryHistory = 200;

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    // Alloy ships a line a few seconds after it's written, stamped with the time it was written,
    // so the newest seconds are left for the next poll
    public static readonly TimeSpan IngestDelay = TimeSpan.FromSeconds(30);

    // Every ban lands here: the web's and the GM tools' go through the game's Ban RPC (gm.ban),
    // and the in-game /ban command is a gm.command
    public const string BanQuery =
        "{server=\"game\", event=\"gm.ban\"} | json | result=~\"online|offline\"";
    public const string BanCommandQuery =
        "{server=\"game\", event=\"gm.command\"} | json | command=\"ban\" | result=\"ok\"";
    public const string CheatQuery = "{server=\"game\", event=\"session.cheat_report\"}";

    private readonly ConcurrentDictionary<(uint WebhookId, string Category), DateTime> lastCategorySent = new();
    private DateTime lastLokiUtc = DateTime.UtcNow - IngestDelay;

    public static bool MatchesCategory(DbWebNotificationWebhook webhook, string category) => category switch
    {
        CategoryBan => webhook.NotifyBans,
        CategoryError => webhook.NotifyErrors,
        CategoryCheatReport => webhook.NotifyCheatReports,
        _ => false,
    };

    public async Task<int> PollOnceAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var webhooks = await db.Webhooks.Where(w => w.Enabled).ToListAsync(ct);
        DateTime untilUtc = nowUtc - IngestDelay;
        if (webhooks.Count == 0)
        {
            this.lastLokiUtc = untilUtc;
            return 0;
        }

        var events = await this.CollectLokiEventsAsync(untilUtc,
            webhooks.Any(w => w.NotifyBans), webhooks.Any(w => w.NotifyErrors),
            webhooks.Any(w => w.NotifyCheatReports), ct);

        if (events.Count == 0)
            return 0;

        int delivered = 0;
        foreach (var ev in events.OrderBy(e => e.TimestampUtc))
        {
            foreach (var webhook in webhooks)
            {
                if (!MatchesCategory(webhook, ev.Category))
                    continue;

                if (webhook.MinIntervalSeconds > 0 &&
                    this.lastCategorySent.TryGetValue((webhook.ID, ev.Category), out var prev) &&
                    nowUtc - prev < TimeSpan.FromSeconds(webhook.MinIntervalSeconds))
                    continue;

                var embed = new DiscordEmbed(ev.Title, ev.Summary, ev.Color, ev.TimestampUtc, ev.Fields);
                var result = await sender.SendAsync(webhook.WebhookUrl, embed, ct);
                var sentAt = DateTime.UtcNow;

                this.lastCategorySent[(webhook.ID, ev.Category)] = nowUtc;
                webhook.LastSentAt = sentAt;
                webhook.LastError = result.Ok ? null : result.Error;

                db.Deliveries.Add(new DbWebNotificationDelivery
                {
                    WebhookID = webhook.ID,
                    WebhookName = webhook.Name,
                    Category = ev.Category,
                    Title = Truncate(ev.Title, 127),
                    Summary = Truncate(ev.Summary, 512),
                    Success = result.Ok,
                    StatusCode = result.StatusCode,
                    Error = result.Error == null ? null : Truncate(result.Error, 255),
                    SentAt = sentAt,
                });

                if (result.Ok)
                    delivered++;
            }
        }

        await db.SaveChangesAsync(ct);
        await PruneDeliveriesAsync(db, ct);
        return delivered;
    }

    private async Task<List<NotificationEvent>> CollectLokiEventsAsync(
        DateTime nowUtc, bool wantBans, bool wantErrors, bool wantCheats, CancellationToken ct)
    {
        var startUtc = this.lastLokiUtc.AddMilliseconds(1);
        if (startUtc >= nowUtc)
            return [];

        var list = new List<NotificationEvent>();
        try
        {
            if (wantCheats)
            {
                var cheats = await loki.QueryRangeAsync(CheatQuery, startUtc, nowUtc, 20, ct);
                foreach (var entry in cheats)
                    list.Add(FromLokiEntry(CategoryCheatReport, "Cheat report", DiscordWebhookSender.ColorCheatReport, entry));
            }

            if (wantErrors)
            {
                var errors = await loki.QueryRangeAsync(LogStats.ErrorSelector, startUtc, nowUtc, 20, ct);
                foreach (var entry in errors)
                    list.Add(FromLokiEntry(CategoryError, $"Server error ({entry.Server})", DiscordWebhookSender.ColorError, entry));
            }

            if (wantBans)
            {
                foreach (var query in new[] { BanQuery, BanCommandQuery })
                    foreach (var entry in await loki.QueryRangeAsync(query, startUtc, nowUtc, 20, ct))
                        list.Add(FromLokiEntry(CategoryBan, "Ban", DiscordWebhookSender.ColorBan, entry));
            }

            this.lastLokiUtc = nowUtc;
        }
        catch (LokiUnavailableException)
        {
            // Keep lastLokiUtc moving slowly so a long outage doesn't replay hours of logs
            if (nowUtc - this.lastLokiUtc > TimeSpan.FromMinutes(5))
                this.lastLokiUtc = nowUtc.AddMinutes(-5);
        }

        return list;
    }

    private static NotificationEvent FromLokiEntry(string category, string title, int color, LogEntry entry)
    {
        var fields = new List<DiscordEmbedField>();
        if (!string.IsNullOrWhiteSpace(entry.Server))
            fields.Add(new("Server", entry.Server));
        if (!string.IsNullOrWhiteSpace(entry.Event))
            fields.Add(new("Event", entry.Event));
        if (entry.AccountId != null)
            fields.Add(new("Account", entry.AccountId.Value.ToString(CultureInfo.InvariantCulture)));
        if (entry.CharacterId != null)
            fields.Add(new("Character", entry.CharacterId.Value.ToString(CultureInfo.InvariantCulture)));

        string description = entry.Message;
        if (!string.IsNullOrWhiteSpace(entry.Exception))
            description += "\n```\n" + Truncate(entry.Exception, 1000) + "\n```";

        return new NotificationEvent(category, title, description, color, entry.Timestamp, fields.ToArray());
    }

    public static async Task PruneDeliveriesAsync(NotificationsDbContext db, CancellationToken ct)
    {
        var cutoff = await db.Deliveries.AsNoTracking()
            .OrderByDescending(x => x.ID)
            .Skip(MaxDeliveryHistory)
            .Select(x => (ulong?)x.ID)
            .FirstOrDefaultAsync(ct);

        if (cutoff != null)
        {
            await db.Deliveries.Where(x => x.ID <= cutoff.Value).ExecuteDeleteAsync(ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await this.PollOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                LogEvents.For<NotificationDispatcher>(WebEvents.Error)
                    .Error(e, "Notification dispatcher tick failed");
            }
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}

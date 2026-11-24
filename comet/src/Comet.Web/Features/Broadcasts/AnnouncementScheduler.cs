namespace Comet.Web.Features.Broadcasts;

using Comet.Core.Logging;
using Comet.Shared.Models.Admin;
using Comet.Web.Game;
using Microsoft.EntityFrameworkCore;

public sealed class AnnouncementScheduler(
    IDbContextFactory<BroadcastsDbContext> dbFactory,
    GameRpc rpc) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    public static bool IsDue(DbWebAnnouncement row, DateTime now)
    {
        if (!row.Enabled || row.IntervalMinutes == 0 || string.IsNullOrWhiteSpace(row.Message))
            return false;

        if (row.StartAt != null && now < DateTime.SpecifyKind(row.StartAt.Value, DateTimeKind.Utc))
            return false;

        if (row.EndAt != null && now >= DateTime.SpecifyKind(row.EndAt.Value, DateTimeKind.Utc))
            return false;

        if (row.LastSentAt == null)
            return true;

        return now >= DateTime.SpecifyKind(row.LastSentAt.Value, DateTimeKind.Utc).AddMinutes(row.IntervalMinutes);
    }

    public async Task<int> RunDueAsync(DateTime now, CancellationToken ct = default)
    {
        if (!rpc.Connected)
            return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Announcements.Where(x => x.Enabled).ToListAsync(ct);
        int sent = 0;

        foreach (var row in rows)
        {
            if (!IsDue(row, now))
                continue;

            var channel = Enum.TryParse<BroadcastChannel>(row.Channel, ignoreCase: true, out var parsed)
                ? parsed
                : BroadcastChannel.Center;

            try
            {
                var result = await rpc.CallAsync<AdminResult>("Broadcast", new BroadcastArgs
                {
                    ActorAccountID = 0,
                    Channel = channel,
                    Text = row.Message.Trim(),
                });

                if (result.Ok)
                {
                    row.LastSentAt = now;
                    await db.SaveChangesAsync(ct);
                    sent++;
                }
            }
            catch (GameOfflineException)
            {
                break;
            }
        }

        return sent;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await this.RunDueAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                LogEvents.For<AnnouncementScheduler>(WebEvents.Error)
                    .Error(e, "Scheduled announcement tick failed");
            }
        }
    }
}

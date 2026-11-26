namespace Fetih.Web.Features.Broadcasts;

using System.Security.Claims;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.EntityFrameworkCore;

// Broadcasts sent now, the scheduled announcements and the paid broadcast queue. Errors are
// codes (invalid_message, not_found, …); a GameOfflineException means nothing was sent.
public sealed class BroadcastService(
    IDbContextFactory<BroadcastsDbContext> factory, GameRpc rpc, AuditLog audit)
{
    public const int MaxTitleLength = 127;
    public const int MaxMessageLength = 512;
    public const int MaxIntervalMinutes = 10080;

    public enum State { Active, Scheduled, Expired, Disabled }

    public bool GameConnected => rpc.Connected;

    public async Task<QueuedBroadcastItem[]> QueueAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Queue.AsNoTracking()
            .OrderBy(x => x.Position)
            .Select(x => new QueuedBroadcastItem(
                x.ID, x.Position, x.CharacterID, x.SenderName, x.Message, x.Addition))
            .ToArrayAsync();
    }

    public async Task<string?> SendAsync(ClaimsPrincipal user, BroadcastChannel channel, string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || text.Length > MaxMessageLength)
            return "invalid_message";

        var result = await rpc.CallAsync<AdminResult>("Broadcast", new BroadcastArgs
        {
            ActorAccountID = user.AccountId(),
            Channel = channel,
            Text = text,
        });
        if (!result.Ok)
            return result.Error ?? "broadcast_failed";

        await audit.WriteAsync("broadcast.send", "broadcast", channel.ToString(),
            after: new { channel = channel.ToString(), text },
            summary: $"[{channel}] {Truncate(text, 80)}");
        return null;
    }

    public async Task<AnnouncementDto[]> ListAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Announcements.AsNoTracking().OrderBy(x => x.ID).ToArrayAsync();
        return rows.Select(ToDto).ToArray();
    }

    // Creates when id is null
    public async Task<(AnnouncementDto? Saved, string? Error)> SaveAsync(
        ClaimsPrincipal user, uint? id, SaveAnnouncementRequest request)
    {
        if (Validate(request) is { } error)
            return (null, error);

        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        DbWebAnnouncement? row;
        AnnouncementDto? before = null;
        if (id == null)
        {
            row = new DbWebAnnouncement { CreatedBy = user.AccountId(), CreatedAt = now };
            db.Announcements.Add(row);
        }
        else
        {
            row = await db.Announcements.FirstOrDefaultAsync(x => x.ID == id);
            if (row == null)
                return (null, "not_found");
            before = ToDto(row);
        }

        row.Title = request.Title.Trim();
        row.Channel = request.Channel.ToString();
        row.Message = request.Message.Trim();
        row.IntervalMinutes = request.IntervalMinutes;
        row.Enabled = request.Enabled;
        row.StartAt = request.StartAt?.ToUniversalTime();
        row.EndAt = request.EndAt?.ToUniversalTime();
        row.UpdatedAt = now;
        await db.SaveChangesAsync();

        var after = ToDto(row);
        if (before == null)
            await audit.WriteAsync("announcement.create", "announcement", row.ID.ToString(),
                after: after, summary: $"Created announcement '{row.Title}' ({row.Channel}, {row.IntervalMinutes}m)");
        else
            await audit.WriteAsync("announcement.update", "announcement", row.ID.ToString(),
                before: before, after: after,
                summary: $"Updated announcement '{row.Title}' ({row.Channel}, {row.IntervalMinutes}m)");
        return (after, null);
    }

    public async Task<(AnnouncementDto? Saved, string? Error)> SetEnabledAsync(
        ClaimsPrincipal user, uint id, bool enabled)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return (null, "not_found");
        var dto = ToDto(row);
        return await this.SaveAsync(user, id, new SaveAnnouncementRequest(
            dto.Title, dto.Channel, dto.Message, dto.IntervalMinutes, enabled, dto.StartAt, dto.EndAt));
    }

    public async Task<string?> DeleteAsync(uint id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Announcements.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return "not_found";

        var before = ToDto(row);
        db.Announcements.Remove(row);
        await db.SaveChangesAsync();
        await audit.WriteAsync("announcement.delete", "announcement", id.ToString(),
            before: before, summary: $"Deleted announcement '{row.Title}'");
        return null;
    }

    public async Task<(AnnouncementDto? Sent, string? Error)> SendNowAsync(ClaimsPrincipal user, uint id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Announcements.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return (null, "not_found");

        var channel = ParseChannel(row.Channel);
        var result = await rpc.CallAsync<AdminResult>("Broadcast", new BroadcastArgs
        {
            ActorAccountID = user.AccountId(),
            Channel = channel,
            Text = row.Message.Trim(),
        });
        if (!result.Ok)
            return (null, result.Error ?? "broadcast_failed");

        row.LastSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync("announcement.send_now", "announcement", id.ToString(),
            after: new { channel = channel.ToString(), row.LastSentAt },
            summary: $"Sent announcement '{row.Title}'");
        return (ToDto(row), null);
    }

    public static State StateOf(AnnouncementDto item, DateTime now) =>
        !item.Enabled ? State.Disabled
        : item.StartAt != null && now < item.StartAt ? State.Scheduled
        : item.EndAt != null && now >= item.EndAt ? State.Expired
        : State.Active;

    // When the scheduler sends it next (it checks every 15 seconds), or null for never
    public static DateTime? NextRun(AnnouncementDto item, DateTime now)
    {
        if (!item.Enabled || item.IntervalMinutes == 0 || (item.EndAt != null && now >= item.EndAt))
            return null;
        var next = item.LastSentAt?.AddMinutes(item.IntervalMinutes) ?? now;
        if (item.StartAt != null && next < item.StartAt)
            next = item.StartAt.Value;
        if (next < now)
            next = now;
        return item.EndAt != null && next >= item.EndAt ? null : next;
    }

    public static BroadcastChannel ParseChannel(string channel) =>
        Enum.TryParse<BroadcastChannel>(channel, ignoreCase: true, out var parsed) ? parsed : BroadcastChannel.Center;

    private static string? Validate(SaveAnnouncementRequest request)
    {
        string title = (request.Title ?? "").Trim();
        if (title.Length == 0 || title.Length > MaxTitleLength)
            return "invalid_title";

        string message = (request.Message ?? "").Trim();
        if (message.Length == 0 || message.Length > MaxMessageLength)
            return "invalid_message";

        if (request.IntervalMinutes == 0 || request.IntervalMinutes > MaxIntervalMinutes)
            return "invalid_interval";

        if (request.StartAt != null && request.EndAt != null &&
            request.EndAt.Value.ToUniversalTime() <= request.StartAt.Value.ToUniversalTime())
            return "invalid_schedule_window";

        return null;
    }

    private static AnnouncementDto ToDto(DbWebAnnouncement row) => new(
        row.ID, row.Title, ParseChannel(row.Channel), row.Message, row.IntervalMinutes, row.Enabled,
        Utc(row.StartAt), Utc(row.EndAt), Utc(row.LastSentAt), row.CreatedBy,
        DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc));

    private static DateTime? Utc(DateTime? dt) =>
        dt == null ? null : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}

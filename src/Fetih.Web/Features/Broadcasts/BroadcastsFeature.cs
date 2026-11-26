namespace Fetih.Web.Features.Broadcasts;

using System.Security.Claims;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class BroadcastsFeature : IFeature
{
    public const int MaxTitleLength = 127;
    public const int MaxMessageLength = 512;
    public const int MaxIntervalMinutes = 10080;

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<BroadcastsDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
        services.AddSingleton<AnnouncementScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<AnnouncementScheduler>());
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/broadcasts").RequirePermission(Permission.Moderate);
        group.MapGet("/queue", GetQueueAsync).WithName("getBroadcastQueue");
        group.MapPost("/send", SendAsync).WithName("sendBroadcast");
        group.MapGet("/announcements", ListAnnouncementsAsync).WithName("listAnnouncements");
        group.MapPost("/announcements", CreateAnnouncementAsync).WithName("createAnnouncement");
        group.MapPut("/announcements/{id:int}", UpdateAnnouncementAsync).WithName("updateAnnouncement");
        group.MapDelete("/announcements/{id:int}", DeleteAnnouncementAsync).WithName("deleteAnnouncement");
        group.MapPost("/announcements/{id:int}/send", SendAnnouncementNowAsync).WithName("sendAnnouncementNow");
    }

    private static async Task<Ok<BroadcastQueueResponse>> GetQueueAsync(
        IDbContextFactory<BroadcastsDbContext> factory, GameRpc rpc)
    {
        await using var db = await factory.CreateDbContextAsync();
        var items = await db.Queue.AsNoTracking()
            .OrderBy(x => x.Position)
            .Select(x => new QueuedBroadcastItem(
                x.ID, x.Position, x.CharacterID, x.SenderName, x.Message, x.Addition))
            .ToArrayAsync();
        return TypedResults.Ok(new BroadcastQueueResponse(rpc.Connected, items));
    }

    private static async Task<Results<Ok<SendBroadcastResponse>, JsonHttpResult<ApiError>>> SendAsync(
        SendBroadcastRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        string text = (request.Text ?? "").Trim();
        if (text.Length == 0 || text.Length > MaxMessageLength)
            return ApiError.Result("invalid_message", StatusCodes.Status400BadRequest);

        var result = await rpc.CallAsync<AdminResult>("Broadcast", new BroadcastArgs
        {
            ActorAccountID = user.AccountId(),
            Channel = request.Channel,
            Text = text,
        });
        if (!result.Ok)
            return ApiError.Result(result.Error ?? "broadcast_failed", StatusCodes.Status400BadRequest);

        await audit.WriteAsync("broadcast.send", "broadcast", request.Channel.ToString(),
            after: new { channel = request.Channel.ToString(), text },
            summary: $"[{request.Channel}] {Truncate(text, 80)}");

        return TypedResults.Ok(new SendBroadcastResponse(true, request.Channel, text));
    }

    private static async Task<Ok<AnnouncementDto[]>> ListAnnouncementsAsync(
        IDbContextFactory<BroadcastsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Announcements.AsNoTracking()
            .OrderBy(x => x.ID)
            .ToArrayAsync();
        return TypedResults.Ok(rows.Select(ToDto).ToArray());
    }

    private static async Task<Results<Created<AnnouncementDto>, JsonHttpResult<ApiError>>> CreateAnnouncementAsync(
        SaveAnnouncementRequest request, ClaimsPrincipal user,
        IDbContextFactory<BroadcastsDbContext> factory, AuditLog audit)
    {
        if (Validate(request) is { } error)
            return error;

        var now = DateTime.UtcNow;
        var row = new DbWebAnnouncement
        {
            Title = request.Title.Trim(),
            Channel = request.Channel.ToString(),
            Message = request.Message.Trim(),
            IntervalMinutes = request.IntervalMinutes,
            Enabled = request.Enabled,
            StartAt = request.StartAt?.ToUniversalTime(),
            EndAt = request.EndAt?.ToUniversalTime(),
            CreatedBy = user.AccountId(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var db = await factory.CreateDbContextAsync();
        db.Announcements.Add(row);
        await db.SaveChangesAsync();

        var dto = ToDto(row);
        await audit.WriteAsync("announcement.create", "announcement", row.ID.ToString(),
            after: dto, summary: $"Created announcement '{row.Title}' ({row.Channel}, {row.IntervalMinutes}m)");

        return TypedResults.Created($"/api/broadcasts/announcements/{row.ID}", dto);
    }

    private static async Task<Results<Ok<AnnouncementDto>, JsonHttpResult<ApiError>>> UpdateAnnouncementAsync(
        uint id, SaveAnnouncementRequest request,
        IDbContextFactory<BroadcastsDbContext> factory, AuditLog audit)
    {
        if (Validate(request) is { } error)
            return error;

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Announcements.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var before = ToDto(row);
        row.Title = request.Title.Trim();
        row.Channel = request.Channel.ToString();
        row.Message = request.Message.Trim();
        row.IntervalMinutes = request.IntervalMinutes;
        row.Enabled = request.Enabled;
        row.StartAt = request.StartAt?.ToUniversalTime();
        row.EndAt = request.EndAt?.ToUniversalTime();
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var after = ToDto(row);
        await audit.WriteAsync("announcement.update", "announcement", row.ID.ToString(),
            before: before, after: after,
            summary: $"Updated announcement '{row.Title}' ({row.Channel}, {row.IntervalMinutes}m)");

        return TypedResults.Ok(after);
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> DeleteAnnouncementAsync(
        uint id, IDbContextFactory<BroadcastsDbContext> factory, AuditLog audit)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Announcements.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var before = ToDto(row);
        db.Announcements.Remove(row);
        await db.SaveChangesAsync();

        await audit.WriteAsync("announcement.delete", "announcement", id.ToString(),
            before: before, summary: $"Deleted announcement '{row.Title}'");

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AnnouncementDto>, JsonHttpResult<ApiError>>> SendAnnouncementNowAsync(
        uint id, ClaimsPrincipal user,
        IDbContextFactory<BroadcastsDbContext> factory, GameRpc rpc, AuditLog audit)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Announcements.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var channel = Enum.TryParse<BroadcastChannel>(row.Channel, ignoreCase: true, out var parsed)
            ? parsed
            : BroadcastChannel.Center;

        var result = await rpc.CallAsync<AdminResult>("Broadcast", new BroadcastArgs
        {
            ActorAccountID = user.AccountId(),
            Channel = channel,
            Text = row.Message.Trim(),
        });
        if (!result.Ok)
            return ApiError.Result(result.Error ?? "broadcast_failed", StatusCodes.Status400BadRequest);

        row.LastSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var dto = ToDto(row);
        await audit.WriteAsync("announcement.send_now", "announcement", id.ToString(),
            after: new { channel = channel.ToString(), row.LastSentAt },
            summary: $"Sent announcement '{row.Title}'");

        return TypedResults.Ok(dto);
    }

    private static JsonHttpResult<ApiError>? Validate(SaveAnnouncementRequest request)
    {
        string title = (request.Title ?? "").Trim();
        if (title.Length == 0 || title.Length > MaxTitleLength)
            return ApiError.Result("invalid_title", StatusCodes.Status400BadRequest);

        string message = (request.Message ?? "").Trim();
        if (message.Length == 0 || message.Length > MaxMessageLength)
            return ApiError.Result("invalid_message", StatusCodes.Status400BadRequest);

        if (request.IntervalMinutes == 0 || request.IntervalMinutes > MaxIntervalMinutes)
            return ApiError.Result("invalid_interval", StatusCodes.Status400BadRequest);

        if (request.StartAt != null && request.EndAt != null &&
            request.EndAt.Value.ToUniversalTime() <= request.StartAt.Value.ToUniversalTime())
            return ApiError.Result("invalid_schedule_window", StatusCodes.Status400BadRequest);

        return null;
    }

    private static AnnouncementDto ToDto(DbWebAnnouncement row)
    {
        var channel = Enum.TryParse<BroadcastChannel>(row.Channel, ignoreCase: true, out var parsed)
            ? parsed
            : BroadcastChannel.Center;
        return new AnnouncementDto(
            row.ID,
            row.Title,
            channel,
            row.Message,
            row.IntervalMinutes,
            row.Enabled,
            Utc(row.StartAt),
            Utc(row.EndAt),
            Utc(row.LastSentAt),
            row.CreatedBy,
            DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc));
    }

    private static DateTime? Utc(DateTime? dt) =>
        dt == null ? null : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}

namespace Comet.Web.Features.Notifications;

using System.Security.Claims;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class NotificationsFeature : IFeature
{
    public const int MaxNameLength = 127;
    public const uint MaxIntervalSeconds = 86400;

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<NotificationsDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
        services.AddHttpClient<IDiscordWebhookSender, DiscordWebhookSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/notifications").RequirePermission(Permission.ManageAccounts);
        group.MapGet("/webhooks", ListWebhooksAsync).WithName("listNotificationWebhooks");
        group.MapPost("/webhooks", CreateWebhookAsync).WithName("createNotificationWebhook");
        group.MapPut("/webhooks/{id:int}", UpdateWebhookAsync).WithName("updateNotificationWebhook");
        group.MapDelete("/webhooks/{id:int}", DeleteWebhookAsync).WithName("deleteNotificationWebhook");
        group.MapPost("/webhooks/{id:int}/test", TestWebhookAsync).WithName("testNotificationWebhook");
        group.MapGet("/deliveries", ListDeliveriesAsync).WithName("listNotificationDeliveries");
    }

    private static async Task<Ok<NotificationWebhookDto[]>> ListWebhooksAsync(
        IDbContextFactory<NotificationsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Webhooks.AsNoTracking().OrderBy(x => x.ID).ToArrayAsync();
        return TypedResults.Ok(rows.Select(ToDto).ToArray());
    }

    private static async Task<Results<Created<NotificationWebhookDto>, JsonHttpResult<ApiError>>> CreateWebhookAsync(
        SaveWebhookRequest request, ClaimsPrincipal user,
        IDbContextFactory<NotificationsDbContext> factory, AuditLog audit)
    {
        if (Validate(request, requireUrl: true) is { } error)
            return error;

        var now = DateTime.UtcNow;
        var row = new DbWebNotificationWebhook
        {
            Name = request.Name.Trim(),
            WebhookUrl = request.WebhookUrl!.Trim(),
            Enabled = request.Enabled,
            NotifyBans = request.NotifyBans,
            NotifyErrors = request.NotifyErrors,
            NotifyCheatReports = request.NotifyCheatReports,
            MinIntervalSeconds = request.MinIntervalSeconds,
            CreatedBy = user.AccountId(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var db = await factory.CreateDbContextAsync();
        db.Webhooks.Add(row);
        await db.SaveChangesAsync();

        var dto = ToDto(row);
        await audit.WriteAsync("notification.create", "webhook", row.ID.ToString(),
            after: dto, summary: $"Created Discord webhook '{row.Name}'");

        return TypedResults.Created($"/api/notifications/webhooks/{row.ID}", dto);
    }

    private static async Task<Results<Ok<NotificationWebhookDto>, JsonHttpResult<ApiError>>> UpdateWebhookAsync(
        uint id, SaveWebhookRequest request,
        IDbContextFactory<NotificationsDbContext> factory, AuditLog audit)
    {
        if (Validate(request, requireUrl: false) is { } error)
            return error;

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Webhooks.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var before = ToDto(row);
        row.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.WebhookUrl))
            row.WebhookUrl = request.WebhookUrl.Trim();
        row.Enabled = request.Enabled;
        row.NotifyBans = request.NotifyBans;
        row.NotifyErrors = request.NotifyErrors;
        row.NotifyCheatReports = request.NotifyCheatReports;
        row.MinIntervalSeconds = request.MinIntervalSeconds;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var after = ToDto(row);
        await audit.WriteAsync("notification.update", "webhook", row.ID.ToString(),
            before: before, after: after, summary: $"Updated Discord webhook '{row.Name}'");

        return TypedResults.Ok(after);
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> DeleteWebhookAsync(
        uint id, IDbContextFactory<NotificationsDbContext> factory, AuditLog audit)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Webhooks.FirstOrDefaultAsync(x => x.ID == id);
        if (row == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var before = ToDto(row);
        db.Webhooks.Remove(row);
        await db.SaveChangesAsync();

        await audit.WriteAsync("notification.delete", "webhook", id.ToString(),
            before: before, summary: $"Deleted Discord webhook '{row.Name}'");

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TestWebhookResponse>, JsonHttpResult<ApiError>>> TestWebhookAsync(
        uint id, ClaimsPrincipal user,
        IDbContextFactory<NotificationsDbContext> factory,
        IDiscordWebhookSender sender,
        AuditLog audit,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Webhooks.FirstOrDefaultAsync(x => x.ID == id, ct);
        if (row == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var now = DateTime.UtcNow;
        var embed = new DiscordEmbed(
            "Test notification",
            $"Test webhook triggered by {user.Username()} from the Fetih admin panel.",
            DiscordWebhookSender.ColorTest,
            now);

        var result = await sender.SendAsync(row.WebhookUrl, embed, ct);
        row.LastSentAt = now;
        row.LastError = result.Ok ? null : result.Error;
        db.Deliveries.Add(new DbWebNotificationDelivery
        {
            WebhookID = row.ID,
            WebhookName = row.Name,
            Category = NotificationDispatcher.CategoryTest,
            Title = embed.Title,
            Summary = embed.Description,
            Success = result.Ok,
            StatusCode = result.StatusCode,
            Error = result.Error,
            SentAt = now,
        });
        await db.SaveChangesAsync(ct);
        await NotificationDispatcher.PruneDeliveriesAsync(db, ct);

        await audit.WriteAsync("notification.test", "webhook", id.ToString(),
            after: new { ok = result.Ok, statusCode = result.StatusCode, error = result.Error },
            summary: $"Tested Discord webhook '{row.Name}' ({(result.Ok ? "ok" : "failed")})");

        return TypedResults.Ok(new TestWebhookResponse(result.Ok, result.StatusCode, result.Error));
    }

    private static async Task<Ok<NotificationDeliveryDto[]>> ListDeliveriesAsync(
        IDbContextFactory<NotificationsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Deliveries.AsNoTracking()
            .OrderByDescending(x => x.ID)
            .Take(50)
            .ToArrayAsync();
        return TypedResults.Ok(rows.Select(x => new NotificationDeliveryDto(
            x.ID,
            x.WebhookID,
            x.WebhookName,
            x.Category,
            x.Title,
            x.Summary,
            x.Success,
            x.StatusCode,
            x.Error,
            DateTime.SpecifyKind(x.SentAt, DateTimeKind.Utc))).ToArray());
    }

    private static JsonHttpResult<ApiError>? Validate(SaveWebhookRequest request, bool requireUrl)
    {
        string name = (request.Name ?? "").Trim();
        if (name.Length == 0 || name.Length > MaxNameLength)
            return ApiError.Result("invalid_name", StatusCodes.Status400BadRequest);

        string url = (request.WebhookUrl ?? "").Trim();
        if (requireUrl || url.Length > 0)
        {
            if (!DiscordWebhookSender.IsValidDiscordWebhookUrl(url))
                return ApiError.Result("invalid_webhook_url", StatusCodes.Status400BadRequest);
        }

        if (request.MinIntervalSeconds > MaxIntervalSeconds)
            return ApiError.Result("invalid_interval", StatusCodes.Status400BadRequest);

        return null;
    }

    private static NotificationWebhookDto ToDto(DbWebNotificationWebhook row) => new(
        row.ID,
        row.Name,
        DiscordWebhookSender.MaskWebhookUrl(row.WebhookUrl),
        row.Enabled,
        row.NotifyBans,
        row.NotifyErrors,
        row.NotifyCheatReports,
        row.MinIntervalSeconds,
        row.LastSentAt == null ? null : DateTime.SpecifyKind(row.LastSentAt.Value, DateTimeKind.Utc),
        row.LastError,
        row.CreatedBy,
        DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc));
}

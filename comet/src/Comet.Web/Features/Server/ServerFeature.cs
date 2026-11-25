namespace Comet.Web.Features.Server;

using System.Security.Claims;
using Comet.Game.Admin;
using Comet.Game.Database.Models;
using Comet.Shared.Models.Admin;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class ServerFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<ServerControlDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/server").RequirePermission(Permission.ServerControl);
        group.MapGet("/status", GetStatusAsync).WithName("getServerControlStatus");
        group.MapPost("/shutdown", StartShutdownAsync).WithName("startServerShutdown");
        group.MapPost("/shutdown/cancel", CancelShutdownAsync).WithName("cancelServerShutdown");
        group.MapPut("/maintenance", UpdateMaintenanceAsync).WithName("updateServerMaintenance");
        group.MapPut("/motd", UpdateMotdAsync).WithName("updateServerMotd");
    }

    private static async Task<Ok<ServerControlResponse>> GetStatusAsync(
        IDbContextFactory<ServerControlDbContext> factory, GameRpc rpc)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ID == DbServerSettings.SingletonId);

        DateTime? updatedAt = row == null ? null : DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc);
        if (rpc.Connected)
        {
            try
            {
                var live = await rpc.CallAsync<ServerControlState>("ServerControlStatus");
                return TypedResults.Ok(new ServerControlResponse(
                    GameConnected: true,
                    MaintenanceEnabled: live.MaintenanceEnabled,
                    MaintenanceMessage: live.MaintenanceMessage ?? "",
                    Motd: live.Motd ?? "",
                    ShutdownPending: live.ShutdownPending,
                    ShutdownMode: live.ShutdownMode,
                    ShutdownAt: live.ShutdownAt == null
                        ? null
                        : DateTime.SpecifyKind(live.ShutdownAt.Value, DateTimeKind.Utc),
                    ShutdownReason: live.ShutdownReason ?? "",
                    OnlinePlayers: live.OnlinePlayers,
                    OnlineNonGmPlayers: live.OnlineNonGmPlayers,
                    UpdatedAt: updatedAt));
            }
            catch (GameOfflineException) { }
        }

        return TypedResults.Ok(new ServerControlResponse(
            GameConnected: false,
            MaintenanceEnabled: row?.MaintenanceEnabled ?? false,
            MaintenanceMessage: row?.MaintenanceMessage ?? "",
            Motd: row?.Motd ?? "",
            ShutdownPending: false,
            ShutdownMode: null,
            ShutdownAt: null,
            ShutdownReason: "",
            OnlinePlayers: 0,
            OnlineNonGmPlayers: 0,
            UpdatedAt: updatedAt));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> StartShutdownAsync(
        StartShutdownRequest request, ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        if (request.Seconds < ServerControl.MinCountdownSeconds ||
            request.Seconds > ServerControl.MaxCountdownSeconds)
            return ApiError.Result("invalid_countdown", StatusCodes.Status400BadRequest);

        string reason = (request.Reason ?? "").Trim();
        if (reason.Length > ServerControl.MaxReasonLength)
            return ApiError.Result("invalid_reason", StatusCodes.Status400BadRequest);

        var result = await rpc.CallAsync<AdminResult>("StartShutdown", new ShutdownArgs
        {
            ActorAccountID = user.AccountId(),
            Mode = request.Mode,
            Seconds = request.Seconds,
            Reason = reason,
        });

        if (!result.Ok)
        {
            int status = result.Error == AdminErrors.Busy
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            string code = result.Error == AdminErrors.Busy
                ? "shutdown_pending"
                : result.Error ?? "shutdown_failed";
            return ApiError.Result(code, status);
        }

        string action = request.Mode == ShutdownMode.Restart ? "server.restart" : "server.shutdown";
        string summary = string.IsNullOrEmpty(reason)
            ? $"Scheduled {request.Mode.ToString().ToLowerInvariant()} in {request.Seconds}s"
            : $"Scheduled {request.Mode.ToString().ToLowerInvariant()} in {request.Seconds}s ({reason})";
        await audit.WriteAsync(action, "server", "game",
            after: new { mode = request.Mode.ToString(), seconds = request.Seconds, reason },
            summary: summary);

        return TypedResults.Ok(new ServerActionResponse(true, true));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> CancelShutdownAsync(
        ClaimsPrincipal user, GameRpc rpc, AuditLog audit)
    {
        var result = await rpc.CallAsync<AdminResult>("CancelShutdown", new AdminArgs
        {
            ActorAccountID = user.AccountId(),
        });

        if (!result.Ok)
        {
            int status = result.Error == AdminErrors.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            string code = result.Error == AdminErrors.NotFound
                ? "no_shutdown_pending"
                : result.Error ?? "cancel_failed";
            return ApiError.Result(code, status);
        }

        await audit.WriteAsync("server.shutdown_cancel", "server", "game",
            summary: "Cancelled scheduled shutdown/restart");

        return TypedResults.Ok(new ServerActionResponse(true, true));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> UpdateMaintenanceAsync(
        UpdateMaintenanceRequest request, ClaimsPrincipal user,
        IDbContextFactory<ServerControlDbContext> factory, GameRpc rpc, AuditLog audit)
    {
        string message = (request.Message ?? "").Trim();
        if (message.Length > ServerControl.MaxMaintenanceMessageLength)
            return ApiError.Result("invalid_message", StatusCodes.Status400BadRequest);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Settings.FirstOrDefaultAsync(x => x.ID == DbServerSettings.SingletonId);
        var before = new
        {
            enabled = row?.MaintenanceEnabled ?? false,
            message = row?.MaintenanceMessage ?? "",
        };

        if (row == null)
        {
            row = new DbServerSettings { ID = DbServerSettings.SingletonId };
            db.Settings.Add(row);
        }
        row.MaintenanceEnabled = request.Enabled;
        row.MaintenanceMessage = message;
        row.UpdatedBy = user.AccountId();
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        bool appliedLive = false;
        if (rpc.Connected)
        {
            try
            {
                var result = await rpc.CallAsync<AdminResult>("SetMaintenance", new MaintenanceArgs
                {
                    ActorAccountID = user.AccountId(),
                    Enabled = request.Enabled,
                    Message = message,
                    KickNonGm = request.KickNonGm,
                });
                appliedLive = result.Ok;
            }
            catch (GameOfflineException) { }
        }

        var after = new
        {
            enabled = request.Enabled,
            message,
            kickNonGm = request.KickNonGm,
            appliedLive,
        };
        await audit.WriteAsync("server.maintenance", "server", "game",
            before: before, after: after,
            summary: request.Enabled ? "Enabled maintenance mode" : "Disabled maintenance mode");

        return TypedResults.Ok(new ServerActionResponse(true, appliedLive));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> UpdateMotdAsync(
        UpdateMotdRequest request, ClaimsPrincipal user,
        IDbContextFactory<ServerControlDbContext> factory, GameRpc rpc, AuditLog audit)
    {
        string text = (request.Text ?? "").Trim();
        if (text.Length > ServerControl.MaxMotdLength)
            return ApiError.Result("invalid_motd", StatusCodes.Status400BadRequest);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Settings.FirstOrDefaultAsync(x => x.ID == DbServerSettings.SingletonId);
        var before = new { motd = row?.Motd ?? "" };

        if (row == null)
        {
            row = new DbServerSettings { ID = DbServerSettings.SingletonId };
            db.Settings.Add(row);
        }
        row.Motd = text;
        row.UpdatedBy = user.AccountId();
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        bool appliedLive = false;
        if (rpc.Connected)
        {
            try
            {
                var result = await rpc.CallAsync<AdminResult>("SetMotd", new MotdArgs
                {
                    ActorAccountID = user.AccountId(),
                    Text = text,
                });
                appliedLive = result.Ok;
            }
            catch (GameOfflineException) { }
        }

        await audit.WriteAsync("server.motd", "server", "game",
            before: before, after: new { motd = text, appliedLive },
            summary: text.Length == 0 ? "Cleared MOTD" : $"Updated MOTD ({text.Length} chars)");

        return TypedResults.Ok(new ServerActionResponse(true, appliedLive));
    }
}

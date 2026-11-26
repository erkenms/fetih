namespace Fetih.Web.Features.Server;

using System.Security.Claims;
using Fetih.Game.Admin;
using Fetih.Game.Database;
using Fetih.Game.Database.Models;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.EntityFrameworkCore;

// Server status, shutdown countdown, maintenance mode, MOTD and guild war controls.
// Errors are codes (invalid_countdown, already_running, …); a GameOfflineException means
// the live game RPC could not be reached.
public sealed class ServerControlService(
    IDbContextFactory<ServerControlDbContext> factory, GameRpc rpc, AuditLog audit)
{
    public bool GameConnected => rpc.Connected;

    public async Task<ServerControlResponse> StatusAsync()
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
                return new ServerControlResponse(
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
                    UpdatedAt: updatedAt);
            }
            catch (GameOfflineException) { }
        }

        return new ServerControlResponse(
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
            UpdatedAt: updatedAt);
    }

    public async Task<string?> StartShutdownAsync(ClaimsPrincipal user, ShutdownMode mode, int seconds, string? reason)
    {
        if (seconds < ServerControl.MinCountdownSeconds || seconds > ServerControl.MaxCountdownSeconds)
            return "invalid_countdown";

        string trimmedReason = (reason ?? "").Trim();
        if (trimmedReason.Length > ServerControl.MaxReasonLength)
            return "invalid_reason";

        var result = await rpc.CallAsync<AdminResult>("StartShutdown", new ShutdownArgs
        {
            ActorAccountID = user.AccountId(),
            Mode = mode,
            Seconds = seconds,
            Reason = trimmedReason,
        });

        if (!result.Ok)
            return result.Error == AdminErrors.Busy ? "shutdown_pending" : result.Error ?? "shutdown_failed";

        string action = mode == ShutdownMode.Restart ? "server.restart" : "server.shutdown";
        string summary = string.IsNullOrEmpty(trimmedReason)
            ? $"Scheduled {mode.ToString().ToLowerInvariant()} in {seconds}s"
            : $"Scheduled {mode.ToString().ToLowerInvariant()} in {seconds}s ({trimmedReason})";
        await audit.WriteAsync(action, "server", "game",
            after: new { mode = mode.ToString(), seconds, reason = trimmedReason },
            summary: summary);
        return null;
    }

    public async Task<string?> CancelShutdownAsync(ClaimsPrincipal user)
    {
        var result = await rpc.CallAsync<AdminResult>("CancelShutdown", new AdminArgs
        {
            ActorAccountID = user.AccountId(),
        });

        if (!result.Ok)
            return result.Error == AdminErrors.NotFound ? "no_shutdown_pending" : result.Error ?? "cancel_failed";

        await audit.WriteAsync("server.shutdown_cancel", "server", "game",
            summary: "Cancelled scheduled shutdown/restart");
        return null;
    }

    public async Task<(bool AppliedLive, string? Error)> UpdateMaintenanceAsync(
        ClaimsPrincipal user, bool enabled, string? message, bool kickNonGm)
    {
        string trimmed = (message ?? "").Trim();
        if (trimmed.Length > ServerControl.MaxMaintenanceMessageLength)
            return (false, "invalid_message");

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
        row.MaintenanceEnabled = enabled;
        row.MaintenanceMessage = trimmed;
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
                    Enabled = enabled,
                    Message = trimmed,
                    KickNonGm = kickNonGm,
                });
                appliedLive = result.Ok;
            }
            catch (GameOfflineException) { }
        }

        await audit.WriteAsync("server.maintenance", "server", "game",
            before: before,
            after: new { enabled, message = trimmed, kickNonGm, appliedLive },
            summary: enabled ? "Enabled maintenance mode" : "Disabled maintenance mode");
        return (appliedLive, null);
    }

    public async Task<(bool AppliedLive, string? Error)> UpdateMotdAsync(ClaimsPrincipal user, string? text)
    {
        string trimmed = (text ?? "").Trim();
        if (trimmed.Length > ServerControl.MaxMotdLength)
            return (false, "invalid_motd");

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Settings.FirstOrDefaultAsync(x => x.ID == DbServerSettings.SingletonId);
        var before = new { motd = row?.Motd ?? "" };

        if (row == null)
        {
            row = new DbServerSettings { ID = DbServerSettings.SingletonId };
            db.Settings.Add(row);
        }
        row.Motd = trimmed;
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
                    Text = trimmed,
                });
                appliedLive = result.Ok;
            }
            catch (GameOfflineException) { }
        }

        await audit.WriteAsync("server.motd", "server", "game",
            before: before,
            after: new { motd = trimmed, appliedLive },
            summary: trimmed.Length == 0 ? "Cleared MOTD" : $"Updated MOTD ({trimmed.Length} chars)");
        return (appliedLive, null);
    }

    public async Task<GuildWarInfo> GuildWarStatusAsync(CancellationToken ct = default)
    {
        GuildWarState? live = null;
        if (rpc.Connected)
        {
            try { live = await rpc.CallAsync<GuildWarState>("GuildWarStatus"); }
            catch (GameOfflineException) { }
        }
        await using var db = new ServerDbContext();
        var saved = await db.GuildWar.AsNoTracking().FirstOrDefaultAsync(ct);
        var ids = new[] { saved?.WinnerGuildID ?? 0, saved?.HolderGuildID ?? 0, live?.HolderGuildID ?? 0, live?.WinnerGuildID ?? 0 }
            .Where(i => i != 0).Distinct().ToList();
        var names = await db.Syndicates.AsNoTracking().Where(g => ids.Contains(g.SyndicateID))
            .ToDictionaryAsync(g => g.SyndicateID, g => g.Name, ct);
        NamedGuild? Guild(uint? guildId) => guildId is uint g and not 0
            ? new NamedGuild(g, names.GetValueOrDefault(g) ?? $"#{g}") : null;
        DateTime? wonAt = live?.WonAt ?? saved?.WonAt;
        return new GuildWarInfo(
            live,
            Guild(live?.WinnerGuildID) ?? Guild(saved?.WinnerGuildID),
            Guild(live?.HolderGuildID) ?? Guild(saved?.HolderGuildID),
            live?.RewardClaimed ?? saved?.RewardClaimed ?? false,
            wonAt == null ? null : DateTime.SpecifyKind(wonAt.Value, DateTimeKind.Utc));
    }

    public Task<string?> StartGuildWarAsync(ClaimsPrincipal user) =>
        this.GuildWarActionAsync(user, "StartGuildWar", "guild_war.start");

    public Task<string?> EndGuildWarAsync(ClaimsPrincipal user) =>
        this.GuildWarActionAsync(user, "EndGuildWar", "guild_war.end");

    private async Task<string?> GuildWarActionAsync(ClaimsPrincipal user, string method, string action)
    {
        var result = await rpc.CallAsync<AdminResult>(method, new AdminArgs { ActorAccountID = user.AccountId() });
        if (!result.Ok)
            return action == "guild_war.start" ? "already_running" : "not_running";
        await audit.WriteAsync(action, "guild_war", "1", summary: action == "guild_war.start" ? "Started" : "Ended");
        return null;
    }
}

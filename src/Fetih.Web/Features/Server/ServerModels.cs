namespace Fetih.Web.Features.Server;

using Fetih.Shared.Models.Admin;

public record ServerControlResponse(
    bool GameConnected,
    bool MaintenanceEnabled,
    string MaintenanceMessage,
    string Motd,
    bool ShutdownPending,
    ShutdownMode? ShutdownMode,
    DateTime? ShutdownAt,
    string ShutdownReason,
    int OnlinePlayers,
    int OnlineNonGmPlayers,
    DateTime? UpdatedAt);

public record StartShutdownRequest(ShutdownMode Mode, int Seconds, string? Reason);

public record UpdateMaintenanceRequest(bool Enabled, string? Message, bool KickNonGm);

public record UpdateMotdRequest(string? Text);

public record ServerActionResponse(bool Ok, bool AppliedLive);

public record NamedGuild(uint Id, string Name);

public record GuildWarInfo(
    GuildWarState? Live,
    NamedGuild? Winner,
    NamedGuild? Holder,
    bool RewardClaimed,
    DateTime? WonAt);

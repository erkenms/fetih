namespace Fetih.Web.Features.Broadcasts;

using Fetih.Shared.Models.Admin;

public record SendBroadcastRequest(BroadcastChannel Channel, string Text);

public record SendBroadcastResponse(bool Sent, BroadcastChannel Channel, string Text);

public record QueuedBroadcastItem(
    uint Id,
    uint Position,
    uint CharacterId,
    string SenderName,
    string Message,
    uint Addition);

public record BroadcastQueueResponse(bool GameConnected, QueuedBroadcastItem[] Items);

public record AnnouncementDto(
    uint Id,
    string Title,
    BroadcastChannel Channel,
    string Message,
    uint IntervalMinutes,
    bool Enabled,
    DateTime? StartAt,
    DateTime? EndAt,
    DateTime? LastSentAt,
    uint CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SaveAnnouncementRequest(
    string Title,
    BroadcastChannel Channel,
    string Message,
    uint IntervalMinutes,
    bool Enabled,
    DateTime? StartAt,
    DateTime? EndAt);

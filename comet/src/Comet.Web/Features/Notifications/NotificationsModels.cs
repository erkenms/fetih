namespace Comet.Web.Features.Notifications;

public record NotificationWebhookDto(
    uint Id,
    string Name,
    string MaskedUrl,
    bool Enabled,
    bool NotifyBans,
    bool NotifyErrors,
    bool NotifyCheatReports,
    uint MinIntervalSeconds,
    DateTime? LastSentAt,
    string? LastError,
    uint CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SaveWebhookRequest(
    string Name,
    string? WebhookUrl,
    bool Enabled,
    bool NotifyBans,
    bool NotifyErrors,
    bool NotifyCheatReports,
    uint MinIntervalSeconds);

public record NotificationDeliveryDto(
    ulong Id,
    uint WebhookId,
    string WebhookName,
    string Category,
    string Title,
    string Summary,
    bool Success,
    int? StatusCode,
    string? Error,
    DateTime SentAt);

public record TestWebhookResponse(bool Ok, int? StatusCode, string? Error);

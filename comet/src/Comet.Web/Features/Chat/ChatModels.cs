namespace Comet.Web.Features.Chat;

using Comet.Web.Features.Audit;

public record ChatLogEntry(
    DateTime Timestamp,
    string Event,
    string Channel,
    uint? CharacterId,
    uint? AccountId,
    string Sender,
    string? Recipient,
    string Text,
    string? Command,
    string? Arguments,
    string? Result,
    string RawMessage);

public record ChatMessagesResponse(
    ChatLogEntry[] Entries,
    string Query,
    string GrafanaExploreUrl,
    DateTime From,
    DateTime To,
    bool Truncated);

public record GmCommandsResponse(
    ChatLogEntry[] Commands,
    AuditEntry[] Audit,
    bool CanViewAudit,
    bool LokiAvailable,
    string Query,
    string GrafanaExploreUrl,
    DateTime From,
    DateTime To,
    bool Truncated);

namespace Fetih.Web.Features.ItemHistory;

public sealed record ItemHolderInfo(
    uint ItemId,
    uint TypeId,
    string? ItemName,
    string? ItemIcon,
    uint CharacterId,
    string? CharacterName,
    byte Position,
    uint Package,
    ushort StackAmount,
    byte Plus,
    byte Bless,
    byte Gem1,
    byte Gem2,
    byte Enchant,
    bool Locked);

public sealed record ItemHistoryStep(
    DateTime Timestamp,
    string Event,
    string Message,
    uint? OwnerCharacterId,
    string? OwnerName,
    uint? FromCharacterId,
    string? FromName,
    uint? TypeId,
    Dictionary<string, string> Fields);

public sealed record ItemDuplicateFlag(
    string Kind,
    string Reason,
    uint ItemId,
    uint[] CharacterIds,
    DateTime[] Timestamps);

public sealed record ItemHistoryResponse(
    uint ItemId,
    uint? TypeId,
    string? ItemName,
    string? ItemIcon,
    ItemHolderInfo[] CurrentHolders,
    ItemHistoryStep[] Steps,
    ItemDuplicateFlag[] Duplicates,
    bool LokiAvailable,
    string? LokiError,
    string Query,
    string GrafanaExploreUrl,
    DateTime From,
    DateTime To,
    bool Truncated);

public sealed record ItemDuplicatesResponse(
    ItemDuplicateFlag[] Flags,
    bool LokiAvailable,
    string? LokiError,
    DateTime From,
    DateTime To);

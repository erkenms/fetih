namespace Comet.Web.Features.Pk;

using Comet.Web.Features.Items;

public record PkCharacterRef(uint Id, string Name, byte Level, byte Class);

public record PkSummaryResponse(
    bool GameConnected,
    int JailedTotal,
    int JailedOnline,
    int ActiveTotal,
    int FlashingCount,
    int RedCount,
    int BlackCount,
    int DetainedRedeemable,
    int DetainedExpired,
    int DetainedRedeemed,
    DateTime? SnapshotAt);

public record JailedCharacterView(
    PkCharacterRef Character,
    ushort PkPoints,
    string NameColor,
    bool Flashing,
    bool Online,
    ushort X,
    ushort Y,
    DateTime? JailedSince,
    string? JailedReason,
    uint BailRequired);

public record PkJailResponse(bool GameConnected, JailedCharacterView[] Inmates);

public record ActivePkView(
    PkCharacterRef Character,
    byte Rebirths,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y,
    ushort PkPoints,
    bool Flashing,
    string NameColor,
    string PkMode,
    bool InJail,
    bool Dead);

public record ActivePkResponse(bool GameConnected, DateTime? TakenAt, ActivePkView[] Players);

public record PkLeaderboardEntry(
    int Position,
    PkCharacterRef Character,
    byte Rebirths,
    ushort PkPoints,
    string NameColor,
    bool Flashing,
    bool Online,
    uint MapId,
    string? MapName,
    bool InJail);

public record PkLeaderboardResponse(bool GameConnected, PkLeaderboardEntry[] Entries);

public record DetainedItemInstance(
    uint ItemId,
    uint TypeId,
    byte Plus,
    byte Bless,
    byte Enchant,
    byte Gem1,
    byte Gem2,
    byte Color,
    ushort Amount,
    ushort AmountLimit,
    bool Locked);

public record DetainedItemView(
    uint DetainId,
    uint ItemId,
    DetainedItemInstance? Instance,
    CatalogItem? CatalogItem,
    PkCharacterRef Owner,
    PkCharacterRef Killer,
    uint Cost,
    DateTime DetainedAt,
    DateTime ExpiresAt,
    uint DaysElapsed,
    bool Redeemed,
    string Status);

public record DetainedItemsResponse(
    DetainedItemView[] Items,
    int RedeemableCount,
    int ExpiredCount,
    int RedeemedCount);

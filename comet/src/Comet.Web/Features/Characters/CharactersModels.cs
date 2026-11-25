namespace Comet.Web.Features.Characters;

using Comet.Web.Features.Items;

public record CharacterListItem(
    uint CharacterId,
    uint AccountId,
    string? AccountUsername,
    string Name,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y,
    uint Silver,
    uint ConquerPoints,
    ushort KillPoints,
    bool Online,
    DateTime Registered);

public record CharacterListResponse(
    CharacterListItem[] Items,
    int Total,
    int Page,
    int PageSize,
    bool GameConnected);

public record CharacterItemEntry(
    uint ItemId,
    uint TypeId,
    byte Position,
    ushort Amount,
    ushort AmountLimit,
    ushort StackAmount,
    byte Plus,
    byte Bless,
    byte Enchant,
    byte Gem1,
    byte Gem2,
    byte Color,
    bool Locked,
    uint Package,
    CatalogItem? Catalog);

public record CharacterWarehouseGroup(
    uint PackageId,
    CharacterItemEntry[] Items);

public record CharacterMagicEntry(
    ushort Type,
    string? Name,
    ushort Level,
    uint Experience,
    ushort OldLevel);

public record CharacterProficiencyEntry(
    ushort Type,
    ushort Level,
    uint Experience);

public record CharacterRelationEntry(
    uint CharacterId,
    string Name,
    byte Level,
    byte CurrentClass,
    DateTime? Since);

public record CharacterGuildSummary(
    uint SyndicateId,
    string Name,
    ushort Rank,
    uint SilverDonation,
    uint ConquerPointsDonation,
    uint Joined);

public record CharacterFamilySummary(
    uint FamilyId,
    string Name,
    ushort Rank,
    ulong Donation,
    uint Joined);

public record CharacterNobilitySummary(
    ulong Donation);

public record CharacterFlowerSummary(
    uint RedRoses,
    uint RedRosesToday,
    uint Lilies,
    uint LiliesToday,
    uint Orchids,
    uint OrchidsToday,
    uint Tulips,
    uint TulipsToday);

public record CharacterArenaSummary(
    uint Points,
    uint WinsToday,
    uint LossesToday,
    uint WinsTotal,
    uint LossesTotal,
    uint HonorPoints,
    uint HonorTotal);

public record CharacterDetailResponse(
    uint CharacterId,
    uint AccountId,
    string? AccountUsername,
    ushort? AccountStatusId,
    ushort? AccountAuthorityId,
    string Name,
    string Spouse,
    uint Mesh,
    ushort Hairstyle,
    byte Level,
    ulong Experience,
    byte CurrentClass,
    byte PreviousClass,
    byte AncestorClass,
    byte Rebirths,
    uint Silver,
    uint StoredSilver,
    uint ConquerPoints,
    ushort Strength,
    ushort Agility,
    ushort Vitality,
    ushort Spirit,
    ushort AttributePoints,
    uint Health,
    uint? MaxHealth,
    uint Mana,
    uint? MaxMana,
    ushort KillPoints,
    uint Virtue,
    uint VipLevel,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y,
    DateTime? MutedUntil,
    DateTime Registered,
    bool Online,
    bool Dead,
    string? IpAddress,
    DateTime? LoggedInAt,
    bool GameConnected,
    CharacterItemEntry[] Equipment,
    CharacterItemEntry[] Inventory,
    CharacterWarehouseGroup[] Warehouses,
    CharacterItemEntry[] Detained,
    CharacterMagicEntry[] Magics,
    CharacterProficiencyEntry[] Proficiencies,
    CharacterRelationEntry[] Friends,
    CharacterRelationEntry[] Enemies,
    CharacterRelationEntry[] TradePartners,
    CharacterGuildSummary? Guild,
    CharacterFamilySummary? Family,
    CharacterNobilitySummary? Nobility,
    CharacterFlowerSummary? Flowers,
    CharacterArenaSummary? Arena);

public record KickCharacterRequest(string? Reason = null);

public record MuteCharacterRequest(int Minutes);

public record MessageCharacterRequest(string Text);

public record GiveItemRequest(
    uint TypeId,
    int Count = 1,
    byte Plus = 0,
    byte Bless = 0,
    byte? Gem1 = null,
    byte? Gem2 = null);

public record TakeItemRequest(uint ItemId);

public record AdjustAmountRequest(long Amount);

public record SetLevelRequest(byte Level);

public record TeleportCharacterRequest(uint MapId, ushort X, ushort Y);

public record CharacterActionResponse(bool Ok, bool Online);

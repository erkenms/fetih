namespace Comet.Web.Features.Catalogs;

public sealed record MonsterCatalogEntry(
    uint Id,
    string Name,
    uint? Lookface,
    int? Level,
    int? Life,
    int? AttackMin,
    int? AttackMax,
    int? Defense,
    int? Dexterity,
    int? Dodge,
    int? AttackRange,
    int? ViewRange,
    int? AttackSpeed,
    int? MoveSpeed,
    int? AttackUser,
    int? DropMoney,
    uint? DropHp,
    uint? DropMp,
    bool Boss,
    uint? MagicType,
    int? MagicHitRate,
    bool InServer,
    bool InClient,
    int? ClientLevel,
    int? ClientMaxLife,
    bool? ClientBoss,
    int? ZoomPercent);

public sealed record MonsterCatalogPage(MonsterCatalogEntry[] Items, int Total);

public sealed record SpellLevelEntry(
    ushort Level,
    ushort UseMana,
    byte UseStamina,
    int Power,
    uint Percent,
    uint StepSeconds,
    uint ActiveTimes,
    uint Range,
    uint Distance,
    int Status,
    uint NeedExperience,
    byte NeedLevel,
    uint UseItem,
    ushort UseItemCount,
    uint DelayMilliseconds);

public sealed record SpellCatalogEntry(
    ushort Type,
    string Name,
    string Sort,
    string? Icon,
    bool Crime,
    bool Ground,
    bool Multi,
    uint Target,
    uint UseXp,
    uint WeaponSubtype,
    uint AutoActive,
    SpellLevelEntry[] Levels);

public sealed record SpellCatalogPage(SpellCatalogEntry[] Items, int Total, string[] Sorts);

public sealed record MapCatalogEntry(
    uint Id,
    string? Name,
    string? ServerName,
    string[] Aliases,
    string? File,
    int? PuzzleSize,
    string? Minimap,
    string? Flags,
    uint? RebornMap);

public sealed record MapCatalogPage(MapCatalogEntry[] Items, int Total);

public sealed record FaceEntry(uint Id, string Path);

public sealed record FacesCatalogResponse(FaceEntry[] Npc, FaceEntry[] Player);

public sealed record ShopItemEntry(
    uint ItemId,
    string? ItemName,
    string? ItemIcon,
    uint? Price,
    string? Currency,
    string? Category,
    string[] Tags,
    string? Description);

public sealed record ShopCatalogEntry(
    string Key,
    string Source,
    uint? NpcId,
    string Name,
    string Currency,
    uint? MapId,
    ushort? X,
    ushort? Y,
    int ItemCount,
    ShopItemEntry[] Items);

public sealed record ShopCatalogResponse(ShopCatalogEntry[] Shops, string[] Sources);

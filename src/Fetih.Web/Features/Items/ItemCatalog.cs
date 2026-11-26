namespace Fetih.Web.Features.Items;

using System.Globalization;
using System.Text.Json;
using Fetih.Game.Items;

public enum ItemCategory
{
    Weapon,
    Armor,
    Headwear,
    Necklace,
    Ring,
    Boots,
    Shield,
    Garment,
    Steed,
    Crop,
    Bottle,
    Arrow,
    Gem,
    Consumable,
    Other,
}

public enum ItemQuality
{
    None,
    Normal,
    Refined,
    Unique,
    Elite,
    Super,
}

public sealed record CatalogItem
{
    public uint Id { get; init; }
    public string Name { get; init; } = "";
    public string? Type { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public string? GroundIcon { get; init; }
    public ItemCategory Category { get; init; }
    public ItemQuality Quality { get; init; }
    public byte RequiredProfession { get; init; }
    public byte RequiredWeaponSkill { get; init; }
    public byte RequiredLevel { get; init; }
    public byte RequiredGender { get; init; }
    public ushort RequiredStrength { get; init; }
    public ushort RequiredAgility { get; init; }
    public ushort RequiredVitality { get; init; }
    public ushort RequiredSpirit { get; init; }
    public byte Monopoly { get; init; }
    public uint Price { get; init; }
    public uint EmoneyPrice { get; init; }
    public uint ActionId { get; init; }
    public ushort MinAttack { get; init; }
    public ushort MaxAttack { get; init; }
    public ushort Defense { get; init; }
    public ushort MagicAttack { get; init; }
    public ushort MagicDefense { get; init; }
    public ushort Dexterity { get; init; }
    public ushort Dodge { get; init; }
    public ushort Life { get; init; }
    public ushort Mana { get; init; }
    public ushort Amount { get; init; }
    public ushort AmountLimit { get; init; }
    public byte Gem1 { get; init; }
    public byte Gem2 { get; init; }
    public ushort AttackRange { get; init; }
    public ushort AttackSpeed { get; init; }
    public uint StackLimit { get; init; }
}

public sealed class ItemCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string? itemsPath;
    private readonly Lock sync = new();
    private DateTime lastWriteUtc = DateTime.MinValue;
    private CatalogSnapshot snapshot = CatalogSnapshot.Empty;

    public ItemCatalog(WebConfiguration config)
        : this(ResolvePath(config.Paths.AssetsDirectory))
    {
    }

    internal ItemCatalog(string? itemsPath)
    {
        this.itemsPath = itemsPath;
    }

    private static string? ResolvePath(string? assetsDir)
    {
        string? resolved = WebConfiguration.PathsSection.Resolve(assetsDir);
        return resolved == null ? null : Path.Combine(resolved, "items.json");
    }

    public bool TryGet(uint id, out CatalogItem? item)
    {
        var current = this.EnsureLoaded();
        return current.ById.TryGetValue(id, out item);
    }

    public CatalogItem[] GetMany(IEnumerable<uint> ids)
    {
        var current = this.EnsureLoaded();
        var results = new List<CatalogItem>();
        foreach (uint id in ids)
        {
            if (current.ById.TryGetValue(id, out var item))
                results.Add(item);
        }
        return [.. results];
    }

    public (CatalogItem[] Items, int Total, bool AssetsAvailable) Search(
        string? q,
        ItemCategory? category,
        ItemQuality? quality,
        byte? profession,
        byte? minLevel,
        byte? maxLevel,
        string? type,
        int page,
        int pageSize)
    {
        var current = this.EnsureLoaded();
        if (!current.AssetsAvailable)
            return ([], 0, false);

        string? query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        string? typeFilter = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        bool numericQuery = query != null && uint.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out uint queryId);
        uint parsedId = numericQuery && uint.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out uint idVal) ? idVal : 0;
        int? professionLine = profession.HasValue ? ProfessionLine(profession.Value) : null;

        var matches = new List<(int Rank, CatalogItem Item)>();
        foreach (var item in current.All)
        {
            if (category.HasValue && item.Category != category.Value)
                continue;
            if (quality.HasValue && item.Quality != quality.Value)
                continue;
            if (professionLine.HasValue)
            {
                if (item.RequiredProfession == 0 || ProfessionLine(item.RequiredProfession) != professionLine.Value)
                    continue;
            }
            if (minLevel.HasValue && item.RequiredLevel < minLevel.Value)
                continue;
            if (maxLevel.HasValue && item.RequiredLevel > maxLevel.Value)
                continue;
            if (typeFilter != null && !string.Equals(item.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (query == null)
            {
                matches.Add((0, item));
                continue;
            }

            int rank = MatchRank(item, query, numericQuery, parsedId);
            if (rank >= 0)
                matches.Add((rank, item));
        }

        matches.Sort((a, b) =>
        {
            int cmp = a.Rank.CompareTo(b.Rank);
            return cmp != 0 ? cmp : a.Item.Id.CompareTo(b.Item.Id);
        });

        int total = matches.Count;
        var paged = matches
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => m.Item)
            .ToArray();

        return (paged, total, true);
    }

    private static int MatchRank(CatalogItem item, string query, bool numericQuery, uint parsedId)
    {
        if (numericQuery)
        {
            if (item.Id == parsedId)
                return 0;
            if (item.Id.ToString(CultureInfo.InvariantCulture).StartsWith(query, StringComparison.Ordinal))
                return 1;
        }

        if (item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 2;
        if ((item.Type != null && item.Type.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (item.Description != null && item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
            return 3;

        return -1;
    }

    private static int ProfessionLine(byte profession) => profession >= 100 ? 10 : profession / 10;

    internal static ItemCategory ClassifyCategory(uint id, uint actionId, ushort life, ushort mana)
    {
        if (ItemSlots.IsOneHanded(id) || ItemSlots.IsTwoHanded(id))
            return ItemCategory.Weapon;
        if (ItemSlots.Fits(id, ItemPosition.Headwear))
            return ItemCategory.Headwear;
        if (ItemSlots.Fits(id, ItemPosition.Necklace))
            return ItemCategory.Necklace;
        if (ItemSlots.Fits(id, ItemPosition.Armor))
            return ItemCategory.Armor;
        if (ItemSlots.Fits(id, ItemPosition.Ring))
            return ItemCategory.Ring;
        if (ItemSlots.Fits(id, ItemPosition.Boots))
            return ItemCategory.Boots;
        if (ItemSlots.IsShield(id))
            return ItemCategory.Shield;
        if (ItemSlots.Fits(id, ItemPosition.Garment))
            return ItemCategory.Garment;
        if (ItemSlots.IsSteed(id))
            return ItemCategory.Steed;
        if (ItemSlots.IsCrop(id))
            return ItemCategory.Crop;
        if (ItemSlots.Fits(id, ItemPosition.Bottle))
            return ItemCategory.Bottle;
        if (ItemSlots.IsArrow(id))
            return ItemCategory.Arrow;
        if (Gems.IsGemItem(id))
            return ItemCategory.Gem;
        if (actionId != 0 || life > 0 || mana > 0)
            return ItemCategory.Consumable;
        return ItemCategory.Other;
    }

    internal static ItemQuality ClassifyQuality(uint id)
    {
        if (!ItemSlots.IsMonsterDrop(id) && !ItemSlots.IsCrop(id))
            return ItemQuality.None;

        return ItemSlots.Quality(id) switch
        {
            3 or 4 or 5 => ItemQuality.Normal,
            6 => ItemQuality.Refined,
            7 => ItemQuality.Unique,
            8 => ItemQuality.Elite,
            9 => ItemQuality.Super,
            _ => ItemQuality.None,
        };
    }

    private CatalogSnapshot EnsureLoaded()
    {
        if (string.IsNullOrEmpty(this.itemsPath) || !File.Exists(this.itemsPath))
            return CatalogSnapshot.Empty;

        DateTime writeTime = File.GetLastWriteTimeUtc(this.itemsPath);
        if (writeTime == this.lastWriteUtc && this.snapshot.AssetsAvailable)
            return this.snapshot;

        lock (this.sync)
        {
            writeTime = File.GetLastWriteTimeUtc(this.itemsPath);
            if (writeTime == this.lastWriteUtc && this.snapshot.AssetsAvailable)
                return this.snapshot;

            using var stream = File.OpenRead(this.itemsPath);
            var raw = JsonSerializer.Deserialize<CatalogItem[]>(stream, JsonOptions) ?? [];
            var enriched = new CatalogItem[raw.Length];
            var byId = new Dictionary<uint, CatalogItem>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                var r = raw[i];
                var item = r with
                {
                    Category = ClassifyCategory(r.Id, r.ActionId, r.Life, r.Mana),
                    Quality = ClassifyQuality(r.Id),
                };
                enriched[i] = item;
                byId[item.Id] = item;
            }

            Array.Sort(enriched, (a, b) => a.Id.CompareTo(b.Id));
            this.snapshot = new CatalogSnapshot(enriched, byId, true);
            this.lastWriteUtc = writeTime;
            return this.snapshot;
        }
    }

    private sealed record CatalogSnapshot(
        CatalogItem[] All,
        Dictionary<uint, CatalogItem> ById,
        bool AssetsAvailable)
    {
        public static readonly CatalogSnapshot Empty = new([], new Dictionary<uint, CatalogItem>(), false);
    }
}

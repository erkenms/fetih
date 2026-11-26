namespace Fetih.Web.Features.Catalogs;

using System.Globalization;

internal sealed record ParsedShopItem(
    uint ItemId,
    uint? Price,
    string? Currency,
    string? Category,
    string[] Tags,
    string? Description);

internal sealed record ParsedShop(
    string Key,
    string Source,
    uint? NpcId,
    string Name,
    string Currency,
    IReadOnlyList<ParsedShopItem> Items);

internal static class ShopParser
{
    public static IReadOnlyList<ParsedShop> ParseShopDat(string text, string source = "Shop.dat")
    {
        var shops = new List<ParsedShop>();
        var seenNpcs = new HashSet<uint>();
        Dictionary<string, string>? section = null;

        void Flush()
        {
            if (section == null || !TryUInt(section.GetValueOrDefault("ID"), out var npcId))
                return;
            if (!seenNpcs.Add(npcId))
                return;

            string currency = MapCurrency(section.GetValueOrDefault("MoneyType"));
            string name = section.GetValueOrDefault("Name") is { Length: > 0 } n ? n : $"NPC #{npcId}";
            TryUInt(section.GetValueOrDefault("ItemAmount"), out var amount);
            var items = new List<ParsedShopItem>();
            for (uint i = 0; i < amount; i++)
            {
                if (TryUInt(section.GetValueOrDefault("Item" + i), out var itemId))
                    items.Add(new ParsedShopItem(itemId, null, currency, null, [], null));
            }
            shops.Add(new ParsedShop($"{source}:{npcId}", source, npcId, name, currency, items));
        }

        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                section = line.StartsWith("[Shop", StringComparison.OrdinalIgnoreCase)
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : null;
                continue;
            }
            int eq = line.IndexOf('=');
            if (section != null && eq > 0)
                section[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        Flush();
        return shops;
    }

    public static IReadOnlyList<ParsedShop> ParseEmoneyShop(string text, string source = "emoneyshop.ini")
    {
        var shops = new List<ParsedShop>();
        uint? currentId = null;
        string currentCurrency = "cp";
        string currentSection = "item";
        var itemMap = new Dictionary<uint, MutableItem>();

        void Flush()
        {
            if (currentId == null)
                return;
            var items = itemMap.Values.Select(m => m.ToRecord(currentCurrency)).ToList();
            shops.Add(new ParsedShop(
                $"{source}:{currentId.Value}",
                source,
                currentId.Value,
                $"CP Shop #{currentId.Value}",
                currentCurrency,
                items));
            itemMap.Clear();
        }

        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
            {
                if (TryUInt(line[3..], out var id))
                {
                    Flush();
                    currentId = id;
                    currentCurrency = "cp";
                    currentSection = "item";
                }
                continue;
            }
            if (line.StartsWith("MoneyType=", StringComparison.OrdinalIgnoreCase))
            {
                currentCurrency = MapCurrency(line[10..]);
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            if (currentId == null)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            if (!key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) || key.Equals("ItemAmount", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line[(eq + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !TryUInt(parts[0], out var itemId))
                continue;

            var entry = GetOrAdd(itemMap, itemId);
            if (currentSection == "recommend")
                entry.AddTag("recommend");
            else if (string.IsNullOrEmpty(entry.Category))
                entry.Category = currentSection;

            if (parts.Length > 1 && !parts[1].Equals("null", StringComparison.OrdinalIgnoreCase))
                entry.AddTag(parts[1]);
        }
        Flush();
        return shops;
    }

    public static IReadOnlyList<ParsedShop> ParseEmoneyShopV2(string text, string source = "emoneyShopV2.ini")
    {
        var shops = new List<ParsedShop>();
        uint? currentId = null;
        string currentCurrency = "cp";
        bool inRecommend = false;
        var itemMap = new Dictionary<uint, MutableItem>();

        void Flush()
        {
            if (currentId == null)
                return;
            var items = itemMap.Values.Select(m => m.ToRecord(currentCurrency)).ToList();
            shops.Add(new ParsedShop(
                $"{source}:{currentId.Value}",
                source,
                currentId.Value,
                $"CP Shop V2 #{currentId.Value}",
                currentCurrency,
                items));
            itemMap.Clear();
        }

        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
            {
                if (TryUInt(line[3..], out var id))
                {
                    Flush();
                    currentId = id;
                    currentCurrency = "cp";
                    inRecommend = false;
                }
                continue;
            }
            if (line.StartsWith("MoneyType=", StringComparison.OrdinalIgnoreCase))
            {
                currentCurrency = MapCurrency(line[10..]);
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inRecommend = line[1..^1].Trim().Equals("recommend", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (currentId == null)
                continue;

            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().StartsWith("Item", StringComparison.OrdinalIgnoreCase))
            {
                var rhs = line[(eq + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (rhs.Length > 0 && TryUInt(rhs[0], out var recId))
                {
                    var rec = GetOrAdd(itemMap, recId);
                    if (inRecommend)
                        rec.AddTag("recommend");
                }
                continue;
            }

            var fields = line.Split([' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0 || !TryUInt(fields[0], out var itemId))
                continue;

            var entry = GetOrAdd(itemMap, itemId);
            if (inRecommend)
                entry.AddTag("recommend");

            if (fields.Length >= 2)
            {
                var meta = fields[1].Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (meta.Length > 0 && string.IsNullOrEmpty(entry.Category))
                    entry.Category = meta[0];
                for (int i = 1; i < meta.Length; i++)
                {
                    if (!meta[i].Equals("null", StringComparison.OrdinalIgnoreCase))
                        entry.AddTag(meta[i]);
                }
            }
            if (fields.Length >= 3)
            {
                var desc = fields[2].Replace('~', ' ').Trim();
                if (desc.Length > 0)
                    entry.Description ??= desc;
            }
        }
        Flush();
        return shops;
    }

    public static IReadOnlyList<ParsedShop> ParseOuterwearShop(string text, string source = "outerwearshop.ini")
    {
        string currentSection = "item";
        var itemMap = new Dictionary<uint, MutableItem>();

        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            if (!key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) || key.Equals("ItemAmount", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line[(eq + 1)..].Trim().Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !TryUInt(parts[0], out var itemId))
                continue;

            var entry = GetOrAdd(itemMap, itemId);
            if (currentSection == "recommend")
                entry.AddTag("recommend");
            else if (string.IsNullOrEmpty(entry.Category))
                entry.Category = currentSection;

            if (parts.Length > 1 && !parts[1].Equals("null", StringComparison.OrdinalIgnoreCase))
                entry.AddTag(parts[1]);
        }

        if (itemMap.Count == 0)
            return [];

        return [new ParsedShop(source, source, null, "Outerwear Shop", "cp",
            itemMap.Values.Select(m => m.ToRecord("cp")).ToList())];
    }

    public static IReadOnlyList<ParsedShop> ParseHonorShop(string text, string source = "HonorShop.ini")
    {
        var items = new List<ParsedShopItem>();
        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("ItemAmount=", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split(',');
            if (parts.Length < 2 || !TryUInt(parts[0], out var itemId))
                continue;
            uint? price = TryUInt(parts[1], out var p) ? p : null;
            string? category = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : null;
            var tags = new List<string>();
            for (int i = 4; i < parts.Length; i++)
            {
                var tag = parts[i].Trim();
                if (tag.Length > 0 && !tag.Equals("null", StringComparison.OrdinalIgnoreCase) && !tags.Contains(tag))
                    tags.Add(tag);
            }
            items.Add(new ParsedShopItem(itemId, price, "honor", category, [.. tags], null));
        }

        if (items.Count == 0)
            return [];

        return [new ParsedShop(source, source, null, "Honor Shop", "honor", items)];
    }

    public static IReadOnlyList<ParsedShop> ParseRacePointShop(string text, string source = "RacePointShop.ini")
    {
        string currentSection = "Normal";
        var itemMap = new Dictionary<uint, MutableItem>();

        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !TryUInt(parts[0], out var itemId))
                continue;

            var entry = GetOrAdd(itemMap, itemId);
            if (TryUInt(parts[1], out var price))
                entry.Price ??= price;
            if (currentSection == "recommend")
                entry.AddTag("recommend");
            if (parts.Length > 2 && !parts[2].Equals("null", StringComparison.OrdinalIgnoreCase))
                entry.Category ??= parts[2];
            for (int i = 3; i < parts.Length; i++)
            {
                if (!parts[i].Equals("null", StringComparison.OrdinalIgnoreCase)
                    && !parts[i].Equals("other", StringComparison.OrdinalIgnoreCase))
                    entry.AddTag(parts[i]);
            }
        }

        if (itemMap.Count == 0)
            return [];

        return [new ParsedShop(source, source, null, "Race Point Shop", "race_points",
            itemMap.Values.Select(m => m.ToRecord("race_points")).ToList())];
    }

    private static string MapCurrency(string? moneyType) => (moneyType ?? "0").Trim() switch
    {
        "0" => "silver",
        "1" => "cp",
        "2" => "bound_cp",
        var other => other.Length > 0 ? other : "silver",
    };

    private static string[] SplitLines(string text) =>
        text.TrimStart('\uFEFF').Split('\n');

    private static bool TryUInt(string? value, out uint result) =>
        uint.TryParse((value ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static MutableItem GetOrAdd(Dictionary<uint, MutableItem> map, uint itemId)
    {
        if (!map.TryGetValue(itemId, out var item))
            map[itemId] = item = new MutableItem(itemId);
        return item;
    }

    private sealed class MutableItem(uint itemId)
    {
        public uint? Price { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        private readonly List<string> Tags = [];

        public void AddTag(string tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !this.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                this.Tags.Add(tag);
        }

        public ParsedShopItem ToRecord(string currency) =>
            new(itemId, null, currency, this.Category, [.. this.Tags], this.Description);
    }
}

namespace Comet.Web.Features.ItemHistory;

using System.Globalization;
using System.Text.Json;
using Comet.Web.Features.Logs;

public static class ItemHistoryParser
{
    public static readonly string[] TrackedEvents =
    [
        "item.created",
        "item.bought",
        "trade.completed",
        "booth.sold",
        "item.dropped",
        "item.picked_up",
        "item.sold",
        "item.upgraded",
        "item.composed",
        "item.embedded",
        "item.stored",
        "item.taken_out",
        "item.enchanted",
        "item.refined",
        "item.stabilized",
        "item.locked",
        "item.unlock_started",
        "item.unlocked",
        "item.expired",
        "item.used",
        "item.spent",
        "item.split",
        "item.degraded",
    ];

    public static string BuildItemLogQl(uint itemId) =>
        $"{{server=\"game\", event=~\"item\\\\..+|trade\\\\.completed|booth\\\\.sold\"}} |= \"{itemId.ToString(CultureInfo.InvariantCulture)}\"";

    public static string BuildCreatedLogQl() =>
        "{server=\"game\", event=\"item.created\"}";

    public static bool MatchesItem(LogEntry entry, uint itemId)
    {
        if (entry.Event == "trade.completed")
        {
            return ContainsId(entry.Fields, "itemIds", itemId)
                || ContainsId(entry.Fields, "partnerItemIds", itemId);
        }

        if (entry.Event == "item.composed")
        {
            return TryGetUInt(entry.Fields, "itemId") == itemId
                || TryGetUInt(entry.Fields, "minorId") == itemId;
        }

        return TryGetUInt(entry.Fields, "itemId") == itemId;
    }

    public static ItemHistoryStep ToStep(
        LogEntry entry,
        uint itemId,
        IReadOnlyDictionary<string, uint> characterIdsByName)
    {
        uint? ownerId = entry.CharacterId;
        string? ownerName = GetString(entry.Fields, "name");
        uint? fromId = null;
        string? fromName = null;
        uint? typeId = TryGetUInt(entry.Fields, "typeId");

        if (entry.Event == "trade.completed")
        {
            uint? firstId = entry.CharacterId;
            uint? secondId = TryGetUInt(entry.Fields, "partnerId");
            string? firstName = GetString(entry.Fields, "name");
            string? secondName = GetString(entry.Fields, "partner");

            if (ContainsId(entry.Fields, "itemIds", itemId))
            {
                // First gave the item to Partner
                fromId = firstId ?? Lookup(firstName, characterIdsByName);
                fromName = firstName;
                ownerId = secondId ?? Lookup(secondName, characterIdsByName);
                ownerName = secondName;
                typeId = PairedTypeId(entry.Fields, "itemIds", "typeIds", itemId) ?? typeId;
            }
            else if (ContainsId(entry.Fields, "partnerItemIds", itemId))
            {
                // Partner gave the item to First
                fromId = secondId ?? Lookup(secondName, characterIdsByName);
                fromName = secondName;
                ownerId = firstId ?? Lookup(firstName, characterIdsByName);
                ownerName = firstName;
                typeId = PairedTypeId(entry.Fields, "partnerItemIds", "partnerTypeIds", itemId) ?? typeId;
            }
        }
        else if (entry.Event == "booth.sold")
        {
            // The buyer (name, characterId) bought the item from the owner
            fromName = GetString(entry.Fields, "owner");
            fromId = Lookup(fromName, characterIdsByName);
            ownerName = GetString(entry.Fields, "name");
            ownerId = entry.CharacterId ?? Lookup(ownerName, characterIdsByName);
        }
        else
        {
            ownerId ??= Lookup(ownerName, characterIdsByName);
            if (entry.Event == "item.composed" && TryGetUInt(entry.Fields, "minorId") == itemId)
                typeId = TryGetUInt(entry.Fields, "minorType") ?? typeId;
        }

        return new ItemHistoryStep(
            entry.Timestamp,
            entry.Event,
            entry.Message,
            ownerId,
            ownerName,
            fromId,
            fromName,
            typeId,
            new Dictionary<string, string>(entry.Fields, StringComparer.Ordinal));
    }

    public static ItemDuplicateFlag[] DetectDuplicates(
        uint itemId,
        ItemHolderInfo[] currentHolders,
        ItemHistoryStep[] steps)
    {
        var flags = new List<ItemDuplicateFlag>();

        if (currentHolders.Length > 1)
        {
            flags.Add(new ItemDuplicateFlag(
                "duplicate_live_id",
                $"{currentHolders.Length} live rows share item ID {itemId}.",
                itemId,
                currentHolders.Select(h => h.CharacterId).Distinct().ToArray(),
                []));
        }

        // Ignore stack-merge item.created entries (which carry StackAmount > 1 on an existing stack).
        var creations = steps
            .Where(s => s.Event == "item.created" && !s.Fields.ContainsKey("stackAmount"))
            .ToArray();

        if (creations.Length > 1)
        {
            flags.Add(new ItemDuplicateFlag(
                "multiple_creation_events",
                $"Item {itemId} has {creations.Length} separate item.created events in logs.",
                itemId,
                creations.Where(c => c.OwnerCharacterId.HasValue).Select(c => c.OwnerCharacterId!.Value).Distinct().ToArray(),
                creations.Select(c => c.Timestamp).ToArray()));
        }

        return flags.ToArray();
    }

    public static HashSet<string> CollectCharacterNames(IEnumerable<LogEntry> entries)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            foreach (var key in new[] { "name", "owner", "partner" })
            {
                if (GetString(entry.Fields, key) is { Length: > 0 } name)
                    names.Add(name);
            }
        }
        return names;
    }

    public static bool ContainsId(IReadOnlyDictionary<string, string> fields, string key, uint target)
    {
        foreach (uint id in ParseUIntList(GetString(fields, key)))
        {
            if (id == target)
                return true;
        }
        return false;
    }

    public static uint[] ParseUIntList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        string trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<uint>();
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt32(out uint n))
                            list.Add(n);
                        else if (el.ValueKind == JsonValueKind.String && uint.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                            list.Add(n);
                    }
                    return list.ToArray();
                }
            }
            catch (JsonException)
            {
                // Fall through to delimiter parsing
            }
        }

        var parts = trimmed.Trim('[', ']').Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<uint>(parts.Length);
        foreach (var part in parts)
        {
            if (uint.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint val))
                result.Add(val);
        }
        return result.ToArray();
    }

    public static uint? TryGetUInt(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        return uint.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value)
            ? value
            : null;
    }

    private static string? GetString(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : null;

    private static uint? Lookup(string? name, IReadOnlyDictionary<string, uint> map) =>
        name != null && map.TryGetValue(name, out uint id) ? id : null;

    private static uint? PairedTypeId(IReadOnlyDictionary<string, string> fields, string idsKey, string typesKey, uint itemId)
    {
        var ids = ParseUIntList(GetString(fields, idsKey));
        var types = ParseUIntList(GetString(fields, typesKey));
        int idx = Array.IndexOf(ids, itemId);
        return idx >= 0 && idx < types.Length ? types[idx] : null;
    }
}

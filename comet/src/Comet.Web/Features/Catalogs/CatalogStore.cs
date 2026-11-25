namespace Comet.Web.Features.Catalogs;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comet.Game.Magic;
using Comet.Game.World;

public sealed class CatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? AssetsDir;
    private readonly string? DataDir;
    private readonly string? ClientDir;
    private readonly Lazy<CatalogSnapshot> Snapshot;

    public CatalogStore(WebConfiguration config)
        : this(
            WebConfiguration.PathsSection.Resolve(config.Paths.AssetsDirectory),
            WebConfiguration.PathsSection.Resolve(config.Paths.DataDirectory),
            WebConfiguration.PathsSection.Resolve(config.Paths.ClientDirectory))
    {
    }

    internal CatalogStore(string? assetsDir, string? dataDir, string? clientDir)
    {
        this.AssetsDir = assetsDir;
        this.DataDir = dataDir;
        this.ClientDir = clientDir;
        this.Snapshot = new Lazy<CatalogSnapshot>(this.LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<MonsterCatalogEntry> Monsters => this.Snapshot.Value.Monsters;
    public IReadOnlyDictionary<uint, MonsterCatalogEntry> MonstersById => this.Snapshot.Value.MonstersById;
    public IReadOnlyList<SpellCatalogEntry> Spells => this.Snapshot.Value.Spells;
    public IReadOnlyDictionary<ushort, SpellCatalogEntry> SpellsByType => this.Snapshot.Value.SpellsByType;
    public IReadOnlyList<string> SpellSorts => this.Snapshot.Value.SpellSorts;
    public IReadOnlyList<MapCatalogEntry> Maps => this.Snapshot.Value.Maps;
    public IReadOnlyDictionary<uint, MapCatalogEntry> MapsById => this.Snapshot.Value.MapsById;
    public FacesCatalogResponse Faces => this.Snapshot.Value.Faces;
    public IReadOnlyList<ShopCatalogEntry> Shops => this.Snapshot.Value.Shops;
    public IReadOnlyList<string> ShopSources => this.Snapshot.Value.ShopSources;

    public string? MapDisplayName(uint mapId) =>
        this.MapsById.TryGetValue(mapId, out var m)
            ? (!string.IsNullOrWhiteSpace(m.Name) ? m.Name : m.ServerName)
            : null;

    private CatalogSnapshot LoadAll()
    {
        var monsters = this.LoadMonsters();
        var spells = this.LoadSpells();
        var maps = this.LoadMaps();
        var faces = this.LoadFaces();
        var items = this.LoadItemLookup();
        var npcs = this.LoadNpcLocations();
        var shops = this.LoadShops(items, npcs);

        return new CatalogSnapshot(
            monsters,
            monsters.ToDictionary(m => m.Id),
            spells,
            spells.ToDictionary(s => s.Type),
            spells.Select(s => s.Sort).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            maps,
            maps.ToDictionary(m => m.Id),
            faces,
            shops,
            shops.Select(s => s.Source).Distinct(StringComparer.Ordinal).ToArray());
    }

    private MonsterCatalogEntry[] LoadMonsters()
    {
        var assetPath = ResolveExisting(this.AssetsDir, "monsters.json");
        if (assetPath != null && TryReadJsonNode(assetPath) is JsonArray assetArr)
            return assetArr.OfType<JsonObject>().Select(ParseMonsterObject).OrderBy(m => m.Id).ToArray();

        var dataPath = ResolveExisting(this.DataDir, "monsters.json");
        if (dataPath != null && TryReadJsonNode(dataPath)?["monsters"] is JsonArray dataArr)
            return dataArr.OfType<JsonObject>().Select(ParseMonsterObject).OrderBy(m => m.Id).ToArray();

        return [];
    }

    private static MonsterCatalogEntry ParseMonsterObject(JsonObject obj)
    {
        var client = obj["client"] as JsonObject;
        bool inServer = obj.ContainsKey("level") || obj.ContainsKey("life");
        bool inClient = client != null;

        int? clientLevel = Int(client?["Level"]);
        int? clientMaxLife = Int(client?["MaxLife"]);
        bool? clientBoss = client?["Boss"] is { } b ? Bool(b) : null;
        int? zoom = Int(client?["ZoomPercent"]);

        return new MonsterCatalogEntry(
            Id: UInt(obj["id"]) ?? 0,
            Name: Str(obj["name"]) ?? Str(client?["name"]) ?? "",
            Lookface: UInt(obj["lookface"]),
            Level: Int(obj["level"]),
            Life: Int(obj["life"]),
            AttackMin: Int(obj["attackMin"]),
            AttackMax: Int(obj["attackMax"]),
            Defense: Int(obj["defense"]),
            Dexterity: Int(obj["dexterity"]),
            Dodge: Int(obj["dodge"]),
            AttackRange: Int(obj["attackRange"]),
            ViewRange: Int(obj["viewRange"]),
            AttackSpeed: Int(obj["attackSpeed"]),
            MoveSpeed: Int(obj["moveSpeed"]),
            AttackUser: Int(obj["attackUser"]),
            DropMoney: Int(obj["dropMoney"]),
            DropHp: UInt(obj["dropHp"]),
            DropMp: UInt(obj["dropMp"]),
            Boss: Bool(obj["boss"]) ?? clientBoss ?? false,
            MagicType: UInt(obj["magicType"]),
            MagicHitRate: Int(obj["magicHitRate"]),
            InServer: inServer,
            InClient: inClient,
            ClientLevel: clientLevel,
            ClientMaxLife: clientMaxLife,
            ClientBoss: clientBoss,
            ZoomPercent: zoom);
    }

    private SpellCatalogEntry[] LoadSpells()
    {
        var assetPath = ResolveExisting(this.AssetsDir, "magic.json");
        if (assetPath != null && TryReadJson<SpellCatalogEntry[]>(assetPath) is { } list)
            return list.OrderBy(s => s.Type).ToArray();

        if (this.ClientDir != null)
        {
            var magicDat = MapStore.ResolveFile(this.ClientDir, "ini/magictype.dat");
            if (magicDat != null)
            {
                try
                {
                    var data = File.ReadAllBytes(magicDat);
                    Comet.Game.Items.TqFileCipher.Decrypt(data);
                    var table = MagicTypeTable.Parse(data);
                    return Enumerable.Range(0, ushort.MaxValue + 1)
                        .Where(t => table.Exists((ushort)t))
                        .Select(t =>
                        {
                            ushort type = (ushort)t;
                            var max = table.MaxLevel(type);
                            var levels = Enumerable.Range(0, max + 1)
                                .Select(l => table.TryGet(type, (ushort)l, out var m) ? m : null)
                                .OfType<MagicType>()
                                .ToArray();
                            var first = levels[0];
                            return new SpellCatalogEntry(
                                Type: type,
                                Name: first.Name,
                                Sort: Enum.IsDefined(first.Sort) ? first.Sort.ToString() : ((ushort)first.Sort).ToString(CultureInfo.InvariantCulture),
                                Icon: null,
                                Crime: first.Crime,
                                Ground: first.Ground,
                                Multi: first.Multi,
                                Target: first.Target,
                                UseXp: first.UseXp,
                                WeaponSubtype: first.WeaponSubtype,
                                AutoActive: first.AutoActive,
                                Levels: levels.Select(m => new SpellLevelEntry(
                                    m.Level, m.UseMana, m.UseStamina, m.Power, m.Percent, m.StepSeconds,
                                    m.ActiveTimes, m.Range, m.Distance, m.Status, m.NeedExperience,
                                    m.NeedLevel, m.UseItem, m.UseItemCount, m.DelayMilliseconds)).ToArray());
                        })
                        .ToArray();
                }
                catch
                {
                    return [];
                }
            }
        }

        return [];
    }

    private MapCatalogEntry[] LoadMaps()
    {
        var serverMaps = new Dictionary<uint, (string? Name, string[] Aliases, string? Flags, uint? Reborn)>();
        var dataPath = ResolveExisting(this.DataDir, "maps.json");
        if (dataPath != null && TryReadJsonNode(dataPath)?["maps"] is JsonArray dataArr)
        {
            foreach (var node in dataArr.OfType<JsonObject>())
            {
                if (UInt(node["map"]) is not { } id)
                    continue;
                var aliases = node["aliases"] is JsonArray a
                    ? a.Select(x => Str(x)).OfType<string>().ToArray()
                    : [];
                serverMaps[id] = (Str(node["name"]), aliases, Str(node["flags"]), UInt(node["reborn"]));
            }
        }

        var assetPath = ResolveExisting(this.AssetsDir, "maps.json");
        if (assetPath != null && TryReadJsonNode(assetPath) is JsonArray assetArr)
        {
            return assetArr.OfType<JsonObject>()
                .Select(obj =>
                {
                    uint id = UInt(obj["id"]) ?? 0;
                    serverMaps.TryGetValue(id, out var extra);
                    var aliases = obj["aliases"] is JsonArray a
                        ? a.Select(x => Str(x)).OfType<string>().ToArray()
                        : extra.Aliases ?? [];
                    return new MapCatalogEntry(
                        Id: id,
                        Name: Str(obj["name"]),
                        ServerName: Str(obj["serverName"]) ?? extra.Name,
                        Aliases: aliases,
                        File: Str(obj["file"]),
                        PuzzleSize: Int(obj["puzzleSize"]),
                        Minimap: Str(obj["minimap"]),
                        Flags: extra.Flags,
                        RebornMap: extra.Reborn);
                })
                .OrderBy(m => m.Id)
                .ToArray();
        }

        return serverMaps
            .OrderBy(kv => kv.Key)
            .Select(kv => new MapCatalogEntry(
                Id: kv.Key,
                Name: null,
                ServerName: kv.Value.Name,
                Aliases: kv.Value.Aliases,
                File: null,
                PuzzleSize: null,
                Minimap: null,
                Flags: kv.Value.Flags,
                RebornMap: kv.Value.Reborn))
            .ToArray();
    }

    private FacesCatalogResponse LoadFaces()
    {
        var assetPath = ResolveExisting(this.AssetsDir, "faces.json");
        if (assetPath == null || TryReadJson<FacesAssetJson>(assetPath) is not { } raw)
            return new FacesCatalogResponse([], []);

        return new FacesCatalogResponse(
            Npc: ToFaceEntries(raw.Npc),
            Player: ToFaceEntries(raw.Player));
    }

    private static FaceEntry[] ToFaceEntries(Dictionary<uint, string>? dict) =>
        dict == null
            ? []
            : dict.OrderBy(kv => kv.Key).Select(kv => new FaceEntry(kv.Key, kv.Value)).ToArray();

    private Dictionary<uint, ItemLookup> LoadItemLookup()
    {
        var result = new Dictionary<uint, ItemLookup>();
        var assetPath = ResolveExisting(this.AssetsDir, "items.json");
        if (assetPath == null || TryReadJsonNode(assetPath) is not JsonArray arr)
            return result;

        foreach (var obj in arr.OfType<JsonObject>())
        {
            if (UInt(obj["id"]) is not { } id)
                continue;
            result[id] = new ItemLookup(
                Str(obj["name"]),
                Str(obj["icon"]),
                UInt(obj["price"]),
                UInt(obj["emoneyPrice"]));
        }
        return result;
    }

    private Dictionary<uint, (uint MapId, ushort X, ushort Y)> LoadNpcLocations()
    {
        var result = new Dictionary<uint, (uint, ushort, ushort)>();
        var npcsPath = ResolveExisting(this.DataDir, "npcs.json");
        if (npcsPath == null || TryReadJsonNode(npcsPath)?["npcs"] is not JsonArray arr)
            return result;

        foreach (var obj in arr.OfType<JsonObject>())
        {
            if (UInt(obj["id"]) is { } id
                && UInt(obj["map"]) is { } mapId
                && UInt(obj["x"]) is { } x
                && UInt(obj["y"]) is { } y)
            {
                result.TryAdd(id, (mapId, (ushort)x, (ushort)y));
            }
        }
        return result;
    }

    private ShopCatalogEntry[] LoadShops(
        Dictionary<uint, ItemLookup> items,
        Dictionary<uint, (uint MapId, ushort X, ushort Y)> npcs)
    {
        if (string.IsNullOrEmpty(this.ClientDir) || !Directory.Exists(this.ClientDir))
            return [];

        var parsed = new List<ParsedShop>();
        ReadShopFile(this.ClientDir, "ini/Shop.dat", text => parsed.AddRange(ShopParser.ParseShopDat(text)));
        ReadShopFile(this.ClientDir, "ini/emoneyShopV2.ini", text => parsed.AddRange(ShopParser.ParseEmoneyShopV2(text)));
        ReadShopFile(this.ClientDir, "ini/emoneyshop.ini", text => parsed.AddRange(ShopParser.ParseEmoneyShop(text)));
        ReadShopFile(this.ClientDir, "ini/outerwearshop.ini", text => parsed.AddRange(ShopParser.ParseOuterwearShop(text)));
        ReadShopFile(this.ClientDir, "ini/HonorShop.ini", text => parsed.AddRange(ShopParser.ParseHonorShop(text)));
        ReadShopFile(this.ClientDir, "ini/RacePointShop.ini", text => parsed.AddRange(ShopParser.ParseRacePointShop(text)));

        return parsed.Select(shop =>
        {
            (uint MapId, ushort X, ushort Y)? loc = shop.NpcId is { } npcId && npcs.TryGetValue(npcId, out var pos)
                ? pos
                : null;
            var enrichedItems = shop.Items.Select(item =>
            {
                items.TryGetValue(item.ItemId, out var info);
                uint? price = item.Price ?? (item.Currency switch
                {
                    "cp" or "bound_cp" => info?.EmoneyPrice,
                    "silver" => info?.Price,
                    _ => null,
                });
                return new ShopItemEntry(
                    ItemId: item.ItemId,
                    ItemName: info?.Name,
                    ItemIcon: info?.Icon,
                    Price: price,
                    Currency: item.Currency ?? shop.Currency,
                    Category: item.Category,
                    Tags: item.Tags,
                    Description: item.Description);
            }).ToArray();

            return new ShopCatalogEntry(
                Key: shop.Key,
                Source: shop.Source,
                NpcId: shop.NpcId,
                Name: shop.Name,
                Currency: shop.Currency,
                MapId: loc?.MapId,
                X: loc?.X,
                Y: loc?.Y,
                ItemCount: enrichedItems.Length,
                Items: enrichedItems);
        }).ToArray();
    }

    private static void ReadShopFile(string clientDir, string relativePath, Action<string> parse)
    {
        var path = MapStore.ResolveFile(clientDir, relativePath);
        if (path == null)
            return;
        try
        {
            parse(File.ReadAllText(path, Encoding.Latin1));
        }
        catch
        {
            // Ignore unreadable shop file
        }
    }

    private static string? ResolveExisting(string? directory, string fileName) =>
        string.IsNullOrEmpty(directory) ? null : MapStore.ResolveFile(directory, fileName);

    private static JsonNode? TryReadJsonNode(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch
        {
            return null;
        }
    }

    private static T? TryReadJson<T>(string path) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static int? Int(JsonNode? node)
    {
        if (node == null) return null;
        try
        {
            return node.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : null;
        }
        catch
        {
            return null;
        }
    }

    private static uint? UInt(JsonNode? node)
    {
        if (node == null) return null;
        try
        {
            return node.GetValueKind() == JsonValueKind.Number ? node.GetValue<uint>() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool? Bool(JsonNode? node)
    {
        if (node == null) return null;
        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => Int(node) != 0,
            _ => null,
        };
    }

    private sealed record ItemLookup(string? Name, string? Icon, uint? Price, uint? EmoneyPrice);

    private sealed record FacesAssetJson(Dictionary<uint, string>? Npc, Dictionary<uint, string>? Player);

    private sealed record CatalogSnapshot(
        MonsterCatalogEntry[] Monsters,
        Dictionary<uint, MonsterCatalogEntry> MonstersById,
        SpellCatalogEntry[] Spells,
        Dictionary<ushort, SpellCatalogEntry> SpellsByType,
        string[] SpellSorts,
        MapCatalogEntry[] Maps,
        Dictionary<uint, MapCatalogEntry> MapsById,
        FacesCatalogResponse Faces,
        ShopCatalogEntry[] Shops,
        string[] ShopSources);
}

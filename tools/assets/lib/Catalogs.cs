using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comet.Game.Items;
using Comet.Game.Magic;
using Comet.Game.World;

// Every output of the asset build. Paths in the JSON are relative to the output folder,
// which the web serves as /assets.
sealed class Catalogs(ClientFiles files, Output output, string serverData)
{
    // Raise it when an image's conversion changes, so every image is made again
    public const string Version = "1";

    public List<string> Summary { get; } = new();

    public void BuildAll()
    {
        Items();
        Magic();
        Monsters();
        Faces();
        Maps();
        Site();
    }

    // The website's illustrations, from the launcher's pictures. AutoPatch/1.bmp has the
    // publisher's logo in its top 115 rows, cut off here. The Play/ characters stand on pure
    // green, which becomes transparent.
    private void Site()
    {
        var written = 0;
        foreach (var (source, path, convert) in new (string, string, Func<Rgba, Rgba>)[]
        {
            ("AutoPatch/1.bmp", "site/hero.png", image => Crop(image, 0, 115, image.Width, image.Height - 115)),
            ("Play/MainS.bmp", "site/character1.png", KeyOutGreen),
            ("Play/0/0.bmp", "site/character2.png", KeyOutGreen),
        })
        {
            var stamp = files.Stamp(source);
            if (stamp != null && output.Image(path, $"{source}@{stamp}", () => convert(Images.Decode(files.Read(source)!))))
                written++;
        }
        Summary.Add($"site: {written} illustrations");
    }

    private static Rgba Crop(Rgba image, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            image.Pixels.AsSpan(((y + row) * image.Width + x) * 4, width * 4).CopyTo(pixels.AsSpan(row * width * 4));
        return new Rgba(width, height, pixels);
    }

    private static Rgba KeyOutGreen(Rgba image)
    {
        var pixels = (byte[])image.Pixels.Clone();
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 1] > 150 && pixels[i + 1] - Math.Max(pixels[i], pixels[i + 2]) > 80)
                pixels[i + 3] = 0;
        return new Rgba(image.Width, image.Height, pixels);
    }

    // itemtype.dat fields past the ones ItemTypeTable reads: 53 the type's name, 54 the
    // description ("None" when there's none). Text is GBK with '~' for a space.
    private void Items()
    {
        var decrypted = Decrypted("ini/itemtype.dat");
        var table = ItemTypeTable.Parse((byte[])decrypted.Clone());
        var text = new Dictionary<uint, string[]>();
        foreach (var line in ClientFiles.Gbk.GetString(decrypted).Split('\n'))
        {
            var f = line.TrimEnd('\r').Split("@@");
            if (f.Length > 1 && uint.TryParse(f[0], CultureInfo.InvariantCulture, out var id))
                text.TryAdd(id, f);
        }

        var minIcons = Icons("ani/ItemMinIcon.Ani", "icons/items", id => $"Item{id}", table.All.Select(t => t.ID), "ItemDefault");
        var groundIcons = Icons("ani/MapItemIcon.Ani", "icons/ground", id => $"{id}", table.All.Select(t => t.ID), "Default");

        var items = table.All.OrderBy(t => t.ID).Select(t =>
        {
            var f = text.GetValueOrDefault(t.ID) ?? [];
            return new ItemEntry
            {
                Id = t.ID,
                Name = Text(f.ElementAtOrDefault(1)) ?? t.Name,
                Type = Text(f.ElementAtOrDefault(53)),
                Description = Text(f.ElementAtOrDefault(54)),
                Icon = minIcons.GetValueOrDefault(t.ID),
                GroundIcon = groundIcons.GetValueOrDefault(t.ID),
                RequiredProfession = t.RequiredProfession,
                RequiredWeaponSkill = t.RequiredWeaponSkill,
                RequiredLevel = t.RequiredLevel,
                RequiredGender = t.RequiredGender,
                RequiredStrength = t.RequiredStrength,
                RequiredAgility = t.RequiredAgility,
                RequiredVitality = t.RequiredVitality,
                RequiredSpirit = t.RequiredSpirit,
                Monopoly = t.Monopoly,
                Price = t.Price,
                EmoneyPrice = t.EmoneyPrice,
                ActionId = t.ActionID,
                MinAttack = t.MinAttack,
                MaxAttack = t.MaxAttack,
                Defense = t.Defense,
                MagicAttack = t.MagicAttack,
                MagicDefense = t.MagicDefense,
                Dexterity = t.Dexterity,
                Dodge = t.Dodge,
                Life = t.Life,
                Mana = t.Mana,
                Amount = t.Amount,
                AmountLimit = t.AmountLimit,
                Gem1 = t.Gem1,
                Gem2 = t.Gem2,
                AttackRange = t.AttackRange,
                AttackSpeed = t.AttackSpeed,
                StackLimit = t.StackLimit,
            };
        }).ToList();
        output.Write("items.json", items, omitZero: true);
        Summary.Add($"items.json: {items.Count} items ({table.Skipped} unreadable), " +
            $"{minIcons.Count} with an icon, {groundIcons.Count} with a ground icon");
    }

    private sealed record ItemEntry
    {
        public uint Id { get; init; }
        public string Name { get; init; } = "";
        public string? Type { get; init; }
        public string? Description { get; init; }
        public string? Icon { get; init; }
        public string? GroundIcon { get; init; }
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

    // magictype.dat: one line per spell level, 1 the type and 8 the level
    private void Magic()
    {
        var decrypted = Decrypted("ini/magictype.dat");
        var table = MagicTypeTable.Parse((byte[])decrypted.Clone());
        var keys = new SortedSet<(ushort Type, ushort Level)>();
        foreach (var line in ClientFiles.Gbk.GetString(decrypted).Split('\n'))
        {
            var f = line.Split("@@");
            if (f.Length > 8 && ushort.TryParse(f[1], CultureInfo.InvariantCulture, out var type)
                && ushort.TryParse(f[8], CultureInfo.InvariantCulture, out var level))
                keys.Add((type, level));
        }
        var types = keys.Select(k => k.Type).Distinct().ToList();
        var icons = Icons("ani/Magic.Ani", "icons/magic", type => $"MagicSkillType{type}", types.Select(t => (uint)t), named: type => $"{type}");
        foreach (var (type, path) in Icons("ani/Magic.Ani", "icons/magic", type => $"XpSkillType{type}",
            types.Where(t => !icons.ContainsKey(t)).Select(t => (uint)t), named: type => $"{type}"))
            icons[type] = path;

        var spells = new List<object>();
        foreach (var type in types)
        {
            var levels = keys.Where(k => k.Type == type)
                .Select(k => table.TryGet(k.Type, k.Level, out var m) ? m : null)
                .OfType<MagicType>().ToList();
            if (levels.Count == 0)
                continue;
            var first = levels[0];
            spells.Add(new
            {
                Type = type,
                first.Name,
                Sort = Enum.IsDefined(first.Sort) ? first.Sort.ToString() : ((ushort)first.Sort).ToString(CultureInfo.InvariantCulture),
                Icon = icons.GetValueOrDefault(type),
                first.Crime,
                first.Ground,
                first.Multi,
                first.Target,
                first.UseXp,
                first.WeaponSubtype,
                first.AutoActive,
                Levels = levels.Select(m => new
                {
                    m.Level,
                    m.UseMana,
                    m.UseStamina,
                    m.Power,
                    m.Percent,
                    m.StepSeconds,
                    m.ActiveTimes,
                    m.Range,
                    m.Distance,
                    m.Status,
                    m.NeedExperience,
                    m.NeedLevel,
                    m.UseItem,
                    m.UseItemCount,
                    m.DelayMilliseconds,
                }),
            });
        }
        output.Write("magic.json", spells);
        Summary.Add($"magic.json: {spells.Count} spells, {icons.Count} with an icon");
    }

    // The server's monsters.json, each with the client's monster.dat section of the same ID
    // (its TypeID) under "client"; monsters only the client has come with "client" alone.
    // monster.dat is encrypted like itemtype.dat and holds GBK INI sections named after the
    // monster.
    private void Monsters()
    {
        var client = new Dictionary<uint, JsonObject>();
        var duplicates = 0;
        JsonObject? section = null;
        foreach (var raw in ClientFiles.Gbk.GetString(Decrypted("ini/monster.dat")).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = new JsonObject { ["name"] = line[1..^1] };
                continue;
            }
            var eq = line.IndexOf('=');
            if (section == null || eq <= 0 || line.StartsWith("//"))
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            section[key] = long.TryParse(value, CultureInfo.InvariantCulture, out var number) ? number : value;
            if (key == "TypeID" && number > 0 && !client.TryAdd((uint)number, section))
                duplicates++;
        }

        var server = new SortedDictionary<uint, JsonObject>();
        foreach (var node in ServerJson("monsters.json")?["monsters"]?.AsArray() ?? [])
            if (node is JsonObject monster && monster["id"]?.GetValue<uint>() is uint id)
                server[id] = (JsonObject)monster.DeepClone();

        var monsters = new List<JsonObject>();
        foreach (var id in server.Keys.Union(client.Keys).Order())
        {
            var entry = server.GetValueOrDefault(id) ?? new JsonObject { ["id"] = id, ["name"] = client[id]["name"]!.DeepClone() };
            if (client.TryGetValue(id, out var fromClient))
                entry["client"] = fromClient.DeepClone();
            monsters.Add(entry);
        }
        output.Write("monsters.json", monsters);
        Summary.Add($"monsters.json: {server.Count} server monsters, {client.Count} client monsters " +
            $"({duplicates} repeated TypeIDs ignored), {server.Keys.Count(client.ContainsKey)} in both");
    }

    // Faces are numbered: NPC faces are the avatar a dialog shows, player faces the one picked
    // at character creation
    private void Faces()
    {
        var npc = NumberedIcons("ani/NpcFace.Ani", "Face", "faces/npc");
        var player = NumberedIcons("ani/PlayerFace.Ani", "Face", "faces/player");
        output.Write("faces.json", new { Npc = npc, Player = player });
        Summary.Add($"faces.json: {npc.Count} NPC faces, {player.Count} player faces");
    }

    private SortedDictionary<uint, string> NumberedIcons(string ani, string prefix, string folder)
    {
        var sections = Ani.Parse(files.Read(ani) ?? []);
        var ids = sections.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => uint.TryParse(k[prefix.Length..], CultureInfo.InvariantCulture, out var n) ? n : (uint?)null)
            .OfType<uint>();
        return new SortedDictionary<uint, string>(Icons(ani, folder, n => $"{prefix}{n}", ids, named: n => $"{n}"));
    }

    // GameMap.dat's maps, with the name the client shows (GameMapEx.ini), the server's /tp
    // name and aliases from maps.json, and the minimap
    private void Maps()
    {
        var index = GameMapIndex.Parse(files.Read("ini/GameMap.dat") ?? throw new FileNotFoundException("No ini/GameMap.dat"));
        var clientNames = new Dictionary<uint, string>();
        uint? current = null;
        foreach (var raw in ClientFiles.Gbk.GetString(files.Read("ini/GameMapEx.ini") ?? []).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
                current = uint.TryParse(line[1..^1], CultureInfo.InvariantCulture, out var id) ? id : null;
            else if (current is uint id && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                clientNames.TryAdd(id, line[5..].Trim());
        }
        var server = new Dictionary<uint, JsonObject>();
        foreach (var node in ServerJson("maps.json")?["maps"]?.AsArray() ?? [])
            if (node is JsonObject map && map["map"]?.GetValue<uint>() is uint id)
                server.TryAdd(id, map);

        var minimaps = Minimaps(index.Keys);
        var maps = index.Values.OrderBy(m => m.Id).Select(m => new
        {
            m.Id,
            Name = clientNames.GetValueOrDefault(m.Id),
            ServerName = server.GetValueOrDefault(m.Id)?["name"]?.GetValue<string>(),
            Aliases = server.GetValueOrDefault(m.Id)?["aliases"]?.AsArray().Select(a => a!.GetValue<string>()).ToList(),
            File = m.Path,
            m.PuzzleSize,
            Minimap = minimaps.GetValueOrDefault(m.Id),
        }).ToList();
        output.Write("maps.json", maps);
        Summary.Add($"maps.json: {maps.Count} maps, {minimaps.Count} with a minimap");
    }

    // MiniMap.Ani: one section per map ID. A section with four frames is a 2x2 grid of
    // quarters (a top left, b top right, c bottom left, d bottom right), joined here.
    private Dictionary<uint, string> Minimaps(IEnumerable<uint> mapIds)
    {
        var sections = Ani.Parse(files.Read("ani/MiniMap.Ani") ?? []);
        var result = new Dictionary<uint, string>();
        var lockResult = new object();
        Parallel.ForEach(mapIds, id =>
        {
            if (!sections.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var frames) || frames.Count == 0)
                return;
            var parts = frames.Count == 4 ? frames : frames.Take(1).ToList();
            var stamps = parts.Select(files.Stamp).ToList();
            if (stamps.Any(s => s == null))
                return;
            var path = $"minimaps/{id}.png";
            var ok = output.Image(path, string.Join("+", parts.Zip(stamps, (p, s) => $"{p}@{s}")), () =>
            {
                var images = parts.Select(p => Images.Decode(files.Read(p)!)).ToList();
                return images.Count == 1 ? images[0] : Images.Join(images[0], images[1], images[2], images[3]);
            });
            if (ok)
                lock (lockResult)
                    result[id] = path;
        });
        return result;
    }

    // Converts the first frame of each wanted .ani section to PNG. With `named`, the picture of
    // each ID is folder/<named(id)>.png; without, each source file is converted once, to
    // folder/<its file name>.png, and IDs that share it share the PNG. Returns the path per ID,
    // for the IDs whose picture the client has.
    private Dictionary<uint, string> Icons(string ani, string folder, Func<uint, string> section, IEnumerable<uint> ids,
        string? fallback = null, Func<uint, string>? named = null)
    {
        var sections = Ani.Parse(files.Read(ani) ?? throw new FileNotFoundException($"No {ani}"));
        var sources = new Dictionary<uint, string>();
        foreach (var id in ids)
            if (sections.TryGetValue(section(id), out var frames) && frames.Count > 0)
                sources[id] = frames[0];
        if (fallback != null && sections.TryGetValue(fallback, out var defaults) && defaults.Count > 0)
            Convert(defaults[0], $"{folder}/default.png");

        var paths = new Dictionary<uint, string>();
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (named != null)
            foreach (var (id, source) in sources)
                targets[paths[id] = $"{folder}/{named(id)}.png"] = source;
        else
        {
            // Sources from different folders may share a file name; later ones get a suffix
            var bySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources.Values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(source).ToLowerInvariant();
                var path = $"{folder}/{name}.png";
                for (var n = 2; targets.ContainsKey(path); n++)
                    path = $"{folder}/{name}-{n}.png";
                targets[path] = source;
                bySource[source] = path;
            }
            foreach (var (id, source) in sources)
                paths[id] = bySource[source];
        }
        var made = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        Parallel.ForEach(targets, target =>
        {
            if (Convert(target.Value, target.Key))
                made[target.Key] = true;
        });
        return paths.Where(p => made.ContainsKey(p.Value)).ToDictionary(p => p.Key, p => p.Value);
    }

    private bool Convert(string source, string path)
    {
        var stamp = files.Stamp(source);
        return stamp != null && output.Image(path, $"{source}@{stamp}", () => Images.Decode(files.Read(source)!));
    }

    private byte[] Decrypted(string path)
    {
        var data = files.Read(path) ?? throw new FileNotFoundException($"No {path} in the client");
        TqFileCipher.Decrypt(data);
        return data;
    }

    private JsonNode? ServerJson(string name)
    {
        var path = Path.Combine(serverData, name);
        if (!File.Exists(path))
        {
            Summary.Add($"{name}: not found in {serverData}, left out");
            return null;
        }
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }

    private static string? Text(string? field)
    {
        if (string.IsNullOrEmpty(field) || field == "None")
            return null;
        return field.Replace('~', ' ');
    }
}

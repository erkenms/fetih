namespace Fetih.Game.Items
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    // What the client's tooltips call a refinery state: the status kind the client keys its
    // own lookups on (CHero::GetMaxLife adds Intensification's power only for 301)
    public enum RefineryKind : uint
    {
        None = 0,
        Intensification = 301,
        FinalDamage = 302,
        FinalAttack = 303,
        Detoxication = 304,
        FinalMagicAttack = 305,
        FinalMagicDamage = 306,
        CriticalStrike = 307,
        SkillCriticalStrike = 308,
        Immunity = 309,
        Breakthrough = 310,
        Counteraction = 311,
        Penetration = 312,
        Block = 313,
        MetalResist = 314,
        WoodResist = 315,
        WaterResist = 316,
        FireResist = 317,
        EarthResist = 318,
        MagicDefense = 319,
    }

    public readonly record struct Refinement(RefineryKind Kind, byte Level, uint Power);

    // Refinery items and Permanent Stones, from the client's ini files:
    // ItemQuench.ini lists the refinery item types; ItemQuenchTipInfo.ini gives each a name,
    // level and power, and names the kinds (sections 301-319); ItemSolidify.ini gives each
    // stone's stabilization points ("type points"); info.ini's [Solidify] lev1-lev5 the points
    // each refinery level needs.
    public sealed class RefineryTable
    {
        public const int MaxLevel = 5;

        private readonly Dictionary<uint, Refinement> Items;
        private readonly Dictionary<uint, uint> Stones;
        private readonly uint[] Needed;

        public RefineryTable(IDictionary<uint, Refinement> items, IDictionary<uint, uint> stones, uint[] needed)
        {
            this.Items = new Dictionary<uint, Refinement>(items);
            this.Stones = new Dictionary<uint, uint>(stones);
            this.Needed = needed;
        }

        public static RefineryTable Empty => new RefineryTable(
            new Dictionary<uint, Refinement>(), new Dictionary<uint, uint>(), new uint[MaxLevel]);

        public int Count => this.Items.Count;

        public bool TryGet(uint itemType, out Refinement refinement) => this.Items.TryGetValue(itemType, out refinement);

        public uint StonePoints(uint itemType) => this.Stones.TryGetValue(itemType, out var points) ? points : 0;

        // The points that stabilize a refinery of this level
        public uint PointsFor(int level) => level >= 1 && level <= this.Needed.Length ? this.Needed[level - 1] : 0;

        public static RefineryTable Load(string clientDirectory)
        {
            string Read(string name)
            {
                var path = Path.Combine(clientDirectory, "ini", name);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"No ini/{name} in {clientDirectory}", path);
                return File.ReadAllText(path, Encoding.Latin1);
            }
            return Parse(Read("ItemQuench.ini"), Read("ItemQuenchTipInfo.ini"), Read("ItemSolidify.ini"), Read("info.ini"));
        }

        public static RefineryTable Parse(string quench, string tips, string solidify, string info)
        {
            var sections = Sections(tips);
            var kinds = new Dictionary<string, RefineryKind>();
            foreach (var (id, keys) in sections.OrderBy(s => s.Key))
                if (Enum.IsDefined(typeof(RefineryKind), id) && id != 0 && keys.TryGetValue("Name", out var name))
                    kinds.TryAdd(name, (RefineryKind)id);

            var items = new Dictionary<uint, Refinement>();
            foreach (var line in quench.Split('\n'))
            {
                if (!uint.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var type) ||
                    !sections.TryGetValue(type, out var keys) ||
                    !keys.TryGetValue("Name", out var name) || !kinds.TryGetValue(name, out var kind))
                    continue;
                byte level = byte.TryParse(keys.GetValueOrDefault("Level"), out var l) ? l : (byte)0;
                uint power = uint.TryParse(keys.GetValueOrDefault("Power"), out var p) ? p : 0;
                if (level >= 1 && level <= MaxLevel)
                    items[type] = new Refinement(kind, level, power);
            }

            var stones = new Dictionary<uint, uint>();
            foreach (var line in solidify.Split('\n'))
            {
                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && uint.TryParse(parts[0], out var type) && uint.TryParse(parts[1], out var points))
                    stones[type] = points;
            }

            var needed = new uint[MaxLevel];
            var solid = IniSection(info, "Solidify");
            for (int level = 1; level <= MaxLevel; level++)
                if (!solid.TryGetValue($"lev{level}", out var value) || !uint.TryParse(value, out needed[level - 1]))
                    throw new InvalidDataException($"info.ini [Solidify] has no lev{level}");
            return new RefineryTable(items, stones, needed);
        }

        private static Dictionary<uint, Dictionary<string, string>> Sections(string text)
        {
            var result = new Dictionary<uint, Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    current = uint.TryParse(line[1..^1], out var id) ? result[id] = new Dictionary<string, string>() : null;
                    continue;
                }
                int equals = line.IndexOf('=');
                if (current != null && equals > 0)
                    current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
            return result;
        }

        // One named section's keys; values lose a trailing "// comment"
        private static Dictionary<string, string> IniSection(string text, string name)
        {
            var result = new Dictionary<string, string>();
            bool inside = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inside = line.Equals($"[{name}]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int equals = line.IndexOf('=');
                if (!inside || equals <= 0)
                    continue;
                var value = line[(equals + 1)..];
                int comment = value.IndexOf("//", StringComparison.Ordinal);
                result[line[..equals].Trim()] = (comment < 0 ? value : value[..comment]).Trim();
            }
            return result;
        }
    }
}

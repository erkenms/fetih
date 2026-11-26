namespace Fetih.Game.States
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using Fetih.Game.Items;

    public enum SubClassId : byte
    {
        None = 0,
        MartialArtist = 1,
        Warlock = 2,
        ChiMaster = 3,
        Sage = 4,
        Apothecary = 5,
        Performer = 6,
        Wrangler = 9,
    }

    public sealed record SubClassPhaseInfo(byte Phase, byte RequiredLevel, byte RequiredRebirths,
        byte RequiredSubLevel, int Effect);

    public sealed record SubClassRequirement(uint ItemTypeId, int Count, string ItemName);

    public sealed record SubClassDefinition(
        SubClassId Id,
        string Title,
        byte MaxLevel,
        uint NpcId,
        IReadOnlyDictionary<byte, SubClassPhaseInfo> Phases,
        IReadOnlyDictionary<byte, ulong> StudyCosts)
    {
        public SubClassPhaseInfo Phase(byte phase) =>
            this.Phases.TryGetValue(phase, out var info) ? info : null;

        public ulong? StudyCostForNextLevel(byte currentLevel) =>
            currentLevel > 0 && currentLevel < this.MaxLevel && this.StudyCosts.TryGetValue(currentLevel, out var cost)
                ? cost
                : null;
    }

    // Sub-class definitions loaded from the 5619 client's ini/SubProfessionInfo.ini (level,
    // rebirth, sub-level requirements, phase effects, NPC IDs) and ini/levexp.dat (study points
    // required to upgrade from each sub-class level 1..8 to the next; encrypted with seed 0x4D2).
    public sealed class SubClassTable
    {
        public const uint LevExpSeed = 0x4D2; // 1234
        public const byte MaxPhase = 9;

        // Item requirements named in SubProfessionInfo.ini's Promotion descriptions, matched to itemtype.dat.
        private static readonly Dictionary<SubClassId, SubClassRequirement> LearnItems = new Dictionary<SubClassId, SubClassRequirement>
        {
            [SubClassId.MartialArtist] = new SubClassRequirement(721259, 5, "Celestial Stone"),
            [SubClassId.Warlock] = new SubClassRequirement(721261, 10, "Bomb"),
            [SubClassId.ChiMaster] = new SubClassRequirement(711188, 1, "Yin Yang Fruit"),
            [SubClassId.Sage] = new SubClassRequirement(723087, 20, "Lucky Amulet"),
            [SubClassId.Apothecary] = new SubClassRequirement(1088002, 10, "Meteor Tear"),
            [SubClassId.Performer] = new SubClassRequirement(753001, 15, "1Orchid"),
            [SubClassId.Wrangler] = new SubClassRequirement(723903, 40, "Saddle"),
        };

        private readonly Dictionary<SubClassId, SubClassDefinition> ById;
        private readonly Dictionary<uint, SubClassDefinition> ByNpc;

        public SubClassTable(IReadOnlyDictionary<SubClassId, SubClassDefinition> definitions)
        {
            this.ById = new Dictionary<SubClassId, SubClassDefinition>(definitions);
            this.ByNpc = new Dictionary<uint, SubClassDefinition>();
            foreach (var def in definitions.Values)
            {
                if (def.NpcId != 0)
                    this.ByNpc[def.NpcId] = def;
            }
        }

        public int Count => this.ById.Count;

        public IEnumerable<SubClassDefinition> All => this.ById.Values;

        public bool TryGet(SubClassId id, out SubClassDefinition def) => this.ById.TryGetValue(id, out def);

        public bool TryGetByNpc(uint npcId, out SubClassDefinition def) => this.ByNpc.TryGetValue(npcId, out def);

        public static SubClassRequirement LearnItemOf(SubClassId id) =>
            LearnItems.TryGetValue(id, out var req) ? req : null;

        public static SubClassTable Load(string clientDirectory)
        {
            var iniPath = Path.Combine(clientDirectory, "ini", "SubProfessionInfo.ini");
            if (!File.Exists(iniPath))
                throw new FileNotFoundException($"No ini/SubProfessionInfo.ini in {clientDirectory}", iniPath);
            var levExpPath = Path.Combine(clientDirectory, "ini", "levexp.dat");
            if (!File.Exists(levExpPath))
                throw new FileNotFoundException($"No ini/levexp.dat in {clientDirectory}", levExpPath);

            var iniText = File.ReadAllText(iniPath, Encoding.Latin1);
            var levExpBytes = File.ReadAllBytes(levExpPath);
            TqFileCipher.Decrypt(levExpBytes, LevExpSeed);
            var levExpText = Encoding.Latin1.GetString(levExpBytes);
            return Parse(iniText, levExpText);
        }

        public static SubClassTable Parse(string iniText, string levExpText)
        {
            var sections = ParseIni(iniText);
            var studyCosts = ParseLevExp(levExpText);

            var definitions = new Dictionary<SubClassId, SubClassDefinition>();
            foreach (var (sectionId, values) in sections)
            {
                if (sectionId is < 1 or > 9 || !Enum.IsDefined(typeof(SubClassId), (byte)sectionId))
                    continue;
                var id = (SubClassId)sectionId;
                string title = values.TryGetValue("title", out var t) ? t.Trim() : id.ToString();
                byte maxLevel = (byte)Math.Clamp(ParseInt(values, "UplimitLev", 9), 1, 9);
                uint npcId = (uint)Math.Max(0, ParseInt(values, "AccessionNpcID", 0));

                var phases = new Dictionary<byte, SubClassPhaseInfo>();
                for (byte p = 1; p <= MaxPhase; p++)
                {
                    int phaseKey = sectionId * 10 + p;
                    if (!sections.TryGetValue(phaseKey, out var pv))
                        continue;
                    byte reqLv = (byte)Math.Clamp(ParseInt(pv, "req_lv", 70), 0, 255);
                    byte reqMeto = (byte)Math.Clamp(ParseInt(pv, "req_meto", 0), 0, 255);
                    byte reqJobLv = (byte)Math.Clamp(ParseInt(pv, "req_joblv", p == 1 ? 0 : p), 0, 255);
                    int effect = ParseInt(pv, "EffectProportion", 0);
                    phases[p] = new SubClassPhaseInfo(p, reqLv, reqMeto, reqJobLv, effect);
                }

                studyCosts.TryGetValue(id, out var costs);
                definitions[id] = new SubClassDefinition(id, title, maxLevel, npcId, phases,
                    costs ?? new Dictionary<byte, ulong>());
            }

            return new SubClassTable(definitions);
        }

        private static Dictionary<int, Dictionary<string, string>> ParseIni(string text)
        {
            var result = new Dictionary<int, Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            using var reader = new StringReader(text);
            string raw;
            while ((raw = reader.ReadLine()) != null)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith(";") || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    current = int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                        ? (result[id] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                        : null;
                    continue;
                }
                if (current == null)
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                current[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        private static Dictionary<SubClassId, Dictionary<byte, ulong>> ParseLevExp(string text)
        {
            var result = new Dictionary<SubClassId, Dictionary<byte, ulong>>();
            using var reader = new StringReader(text);
            string raw;
            while ((raw = reader.ReadLine()) != null)
            {
                var parts = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) ||
                    type <= 0 || type > 255 || !Enum.IsDefined(typeof(SubClassId), (byte)type))
                    continue;
                if (!byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte level) ||
                    !ulong.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong cost))
                    continue;
                var id = (SubClassId)type;
                if (!result.TryGetValue(id, out var costs))
                    result[id] = costs = new Dictionary<byte, ulong>();
                costs[level] = cost;
            }
            return result;
        }

        private static int ParseInt(Dictionary<string, string> values, string key, int fallback) =>
            values.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : fallback;
    }
}

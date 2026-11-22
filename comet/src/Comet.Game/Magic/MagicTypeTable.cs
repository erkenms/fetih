namespace Comet.Game.Magic
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Comet.Game.Items;

    // What a spell does at one of its levels, as the client's ini/magictype.dat has it
    public sealed record MagicType
    {
        public ushort Type { get; init; }
        public MagicSort Sort { get; init; }
        public string Name { get; init; }
        public bool Crime { get; init; }
        public bool Ground { get; init; }
        public bool Multi { get; init; }
        public uint Target { get; init; }
        public ushort Level { get; init; }
        public ushort UseMana { get; init; }

        // Below 30000 a flat amount; from 30000 on a percentage of the attack (30110 is 110%)
        public int Power { get; init; }
        public uint Percent { get; init; }
        public uint StepSeconds { get; init; }

        // The status a spell gives or takes, as magictype numbers it (22 Fly), or -1
        public int Status { get; init; }

        // How many times a spell that lasts acts, every StepSeconds (Toxic Fog's poison)
        public uint ActiveTimes { get; init; }
        public uint Range { get; init; }
        public uint Distance { get; init; }

        // Experience at this level to reach the next, and the character level that needs
        public uint NeedExperience { get; init; }
        public byte NeedLevel { get; init; }

        // 1: an XP skill, cast only with the XP bar full
        public uint UseXp { get; init; }

        // The weapon the spell needs, by item type / 1000 (410 blade), or 0
        public uint WeaponSubtype { get; init; }

        // Not 0: a weapon skill that goes off by itself, at Percent of the holder's swings
        public uint AutoActive { get; init; }
        public byte UseStamina { get; init; }

        // What each cast takes from the left hand: the item's type / 1000 % 1000 (50 for the
        // 1050xxx arrows) and how many; nothing when UseItem is 0
        public uint UseItem { get; init; }
        public ushort UseItemCount { get; init; }
        public uint DelayMilliseconds { get; init; }

        public bool IsXpSkill => this.UseXp == 1;
        public bool IsPassive => this.AutoActive != 0;
        public bool IsPercent => this.Power >= PercentBase;
        public int PowerPercent => this.Power - PercentBase;

        public const int PercentBase = 30000;
    }

    // How a spell picks its targets (magictype's sort)
    public enum MagicSort : ushort
    {
        Attack = 1,
        Recruit = 2,
        Cross = 3,
        Fan = 4,
        Bomb = 5,
        AttachStatus = 6,
        DetachStatus = 7,
        DispatchXp = 11,
        Line = 14,
        AttackStatus = 16,
        AddMana = 20,
        DecLife = 26,
        GroundSting = 27,
        Riding = 32,
        ScurvyBomb = 56,
        CannonBarrage = 57,
        GaleBomb = 60,
        BladeTempest = 61,
        KrakensRevenge = 62,
        BlackbeardsRage = 63,
    }

    public sealed class MagicTypeTable
    {
        private readonly Dictionary<(ushort Type, ushort Level), MagicType> Types;

        public int Count => this.Types.Count;
        public int Skipped { get; }

        public MagicTypeTable(IEnumerable<MagicType> types, int skipped = 0)
        {
            this.Types = new Dictionary<(ushort, ushort), MagicType>();
            foreach (var type in types)
                this.Types[(type.Type, type.Level)] = type;
            this.Skipped = skipped;
        }

        public bool TryGet(ushort type, ushort level, out MagicType magic) =>
            this.Types.TryGetValue((type, level), out magic);

        public bool Exists(ushort type) => this.Types.ContainsKey((type, 0));

        public ushort MaxLevel(ushort type) =>
            this.Types.Keys.Where(k => k.Type == type).Select(k => k.Level).DefaultIfEmpty().Max();

        public static MagicTypeTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "magictype.dat");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/magictype.dat in {clientDirectory}", path);
            var data = File.ReadAllBytes(path);
            TqFileCipher.Decrypt(data);
            return Parse(data);
        }

        // Decrypted, one spell level per line, fields ending in "@@". The 5619 client's
        // 50 fields follow cq_magictype with one unknown field after need_exp (19):
        // 0 id, 1 type, 2 sort, 3 name, 4 crime, 5 ground, 6 multi, 7 target, 8 level,
        // 9 use_mp, 10 power, 11 intone, 12 percent, 13 step_secs, 14 range, 15 distance,
        // 16 status, 17 need_prof, 18 need_exp, 20 need_level, 21 use_xp, 22 weapon_subtype,
        // 23 active_times, 24 auto_active, 29 use_ep, 31 use_item, 33 delay_ms, 34 use_item_num
        public static MagicTypeTable Parse(byte[] decrypted)
        {
            var types = new List<MagicType>();
            var seen = new HashSet<(ushort, ushort)>();
            int skipped = 0;
            foreach (var line in Encoding.Latin1.GetString(decrypted).Split('\n'))
            {
                var record = line.TrimEnd('\r');
                if (record.Length == 0)
                    continue;
                var type = ParseRecord(record.Split("@@"));
                if (type == null || !seen.Add((type.Type, type.Level)))
                    skipped++;
                else
                    types.Add(type);
            }
            return new MagicTypeTable(types, skipped);
        }

        private static MagicType ParseRecord(string[] f)
        {
            if (f.Length < 34)
                return null;
            try
            {
                return new MagicType
                {
                    Type = ushort.Parse(f[1], CultureInfo.InvariantCulture),
                    Sort = (MagicSort)ushort.Parse(f[2], CultureInfo.InvariantCulture),
                    Name = f[3].Replace('~', ' '),
                    Crime = f[4] != "0",
                    Ground = f[5] != "0",
                    Multi = f[6] != "0",
                    Target = UInt(f[7]),
                    Level = ushort.Parse(f[8], CultureInfo.InvariantCulture),
                    UseMana = ushort.Parse(f[9], CultureInfo.InvariantCulture),
                    Power = int.Parse(f[10], CultureInfo.InvariantCulture),
                    Percent = UInt(f[12]),
                    StepSeconds = UInt(f[13]),
                    Range = UInt(f[14]),
                    Distance = UInt(f[15]),
                    Status = int.Parse(f[16], CultureInfo.InvariantCulture),
                    NeedExperience = UInt(f[18]),
                    NeedLevel = byte.Parse(f[20], CultureInfo.InvariantCulture),
                    UseXp = UInt(f[21]),
                    WeaponSubtype = UInt(f[22]),
                    ActiveTimes = UInt(f[23]),
                    AutoActive = UInt(f[24]),
                    UseStamina = byte.Parse(f[29], CultureInfo.InvariantCulture),
                    DelayMilliseconds = UInt(f[33]),
                    UseItem = UInt(f[31]),
                    UseItemCount = ushort.Parse(f[34], CultureInfo.InvariantCulture),
                };
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                return null;
            }
        }

        private static uint UInt(string s) => uint.Parse(s, CultureInfo.InvariantCulture);
    }
}

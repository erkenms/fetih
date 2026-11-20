namespace Comet.Game.Magic
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    // Weapon proficiency levels, from the client's ini/WeaponSkillLevelExp.ini: per line the
    // level, the experience to reach it from the level below, and a number not used here
    public sealed class ProficiencyTable
    {
        private readonly Dictionary<ushort, uint> ToReach;

        public ushort MaxLevel { get; }

        public ProficiencyTable(IEnumerable<(ushort Level, uint Experience)> levels)
        {
            this.ToReach = levels.ToDictionary(l => l.Level, l => l.Experience);
            this.MaxLevel = this.ToReach.Keys.DefaultIfEmpty().Max();
        }

        // Experience to go from this level to the next; 0 at the top. Level 1 needs none, so a
        // new proficiency (level 0) reaches it with its first experience.
        public uint Needed(ushort level) =>
            level < this.MaxLevel && this.ToReach.TryGetValue((ushort)(level + 1), out var exp) ? exp : 0;

        public (ushort Level, uint Experience) Gain(ushort level, uint experience, uint amount)
        {
            ulong total = (ulong)experience + amount;
            while (level < this.MaxLevel && total >= this.Needed(level))
            {
                total -= this.Needed(level);
                level++;
            }
            return (level, level >= this.MaxLevel ? 0 : (uint)total);
        }

        public static ProficiencyTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "WeaponSkillLevelExp.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/WeaponSkillLevelExp.ini in {clientDirectory}", path);
            return Parse(File.ReadAllLines(path));
        }

        public static ProficiencyTable Parse(IEnumerable<string> lines)
        {
            var levels = new List<(ushort, uint)>();
            foreach (var line in lines)
            {
                var f = line.Trim().Split(',');
                if (f.Length < 2 ||
                    !ushort.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var level) ||
                    !uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var exp))
                    continue;
                levels.Add((level, exp));
            }
            if (levels.Count == 0)
                throw new FormatException("No levels in WeaponSkillLevelExp.ini");
            return new ProficiencyTable(levels);
        }
    }
}

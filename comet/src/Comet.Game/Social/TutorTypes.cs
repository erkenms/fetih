namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Comet.Game.Database.Models;

    // Mentor level brackets from the 5619 client's ini/tutorType.ini:
    // minLevel,maxLevel,maxApprentices,maxBattlePower.
    public sealed class TutorTypes
    {
        public const byte DefaultMinMentorLevel = 30;

        public sealed record Bracket(uint MinLevel, uint MaxLevel, uint MaxApprentices, uint MaxBattlePower);

        private readonly Bracket[] Brackets;

        public TutorTypes(IEnumerable<Bracket> brackets)
        {
            this.Brackets = brackets.ToArray();
            this.MinMentorLevel = this.Brackets.Length > 0
                ? (byte)Math.Min(byte.MaxValue, this.Brackets.Min(b => b.MinLevel))
                : DefaultMinMentorLevel;
        }

        public int Count => this.Brackets.Length;
        public byte MinMentorLevel { get; }

        public static TutorTypes Load(string clientDirectory)
        {
            var ini = Path.Combine(clientDirectory, "ini");
            var path = Path.Combine(ini, "tutorType.ini");
            if (!File.Exists(path))
                path = Path.Combine(ini, "TutorType.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/tutorType.ini in {clientDirectory}", path);
            return Parse(File.ReadAllText(path));
        }

        public static TutorTypes Parse(string text)
        {
            var list = new List<Bracket>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;
                var parts = line.Split(',');
                if (parts.Length < 4)
                    continue;
                if (uint.TryParse(parts[0].Trim(), out uint min) &&
                    uint.TryParse(parts[1].Trim(), out uint max) &&
                    uint.TryParse(parts[2].Trim(), out uint apps) &&
                    uint.TryParse(parts[3].Trim(), out uint bp))
                {
                    list.Add(new Bracket(min, max, apps, bp));
                }
            }
            return new TutorTypes(list);
        }

        // Reborn characters count their highest previous life level if it exceeds their current level
        public static uint EffectiveLevel(DbCharacter character)
        {
            uint level = character.Level;
            if (character.Rebirths > 0)
            {
                uint previous = Math.Max(character.FirstLifeLevel, character.SecondLifeLevel);
                if (previous > level)
                    level = previous;
            }
            return level;
        }

        public uint MaxApprentices(DbCharacter mentor)
        {
            uint level = EffectiveLevel(mentor);
            foreach (var b in this.Brackets)
                if (level >= b.MinLevel && level <= b.MaxLevel)
                    return b.MaxApprentices;
            return 0;
        }

        public uint MaxBattlePower(DbCharacter mentor)
        {
            uint level = EffectiveLevel(mentor);
            foreach (var b in this.Brackets)
                if (level >= b.MinLevel && level <= b.MaxLevel)
                    return b.MaxBattlePower;
            return 0;
        }

        public bool CanBeMentor(DbCharacter mentor) =>
            mentor.Level >= this.MinMentorLevel || mentor.Rebirths > 0;

        // Same-rebirth level gap from the 5619 client: 30 at 0 rebirths, 20 at 1, 10 at 2+.
        // Higher rebirth count qualifies regardless of current level.
        public bool MeetsLevelGap(DbCharacter mentor, DbCharacter apprentice)
        {
            if (apprentice.Rebirths > mentor.Rebirths)
                return false;
            if (mentor.Rebirths > apprentice.Rebirths)
                return true;
            int gap = mentor.Rebirths switch
            {
                0 => 30,
                1 => 20,
                _ => 10,
            };
            return mentor.Level - apprentice.Level >= gap;
        }
    }
}

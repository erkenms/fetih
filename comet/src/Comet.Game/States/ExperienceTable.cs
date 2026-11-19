namespace Comet.Game.States
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public sealed class ExperienceTable
    {
        private readonly Dictionary<byte, ulong> Required;

        private sealed record LevelFile(List<LevelExp> Levels);

        // Data/levels.json; a level listed twice is an error
        public static List<LevelExp> Load(string path)
        {
            var levels = DataFiles.Read<LevelFile>(path).Levels ?? new List<LevelExp>();
            var repeated = levels.GroupBy(l => l.Level).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these levels more than once: {string.Join(", ", repeated)}");
            return levels;
        }

        public ExperienceTable(IEnumerable<LevelExp> rows)
        {
            this.Required = rows.Where(r => r.Experience > 0).ToDictionary(r => r.Level, r => r.Experience);
            byte level = 1;
            while (this.Required.ContainsKey(level))
                level++;
            this.MaxLevel = level;
        }

        // The first level without a row; a gap in the table caps leveling there
        public byte MaxLevel { get; }

        public int Count => this.Required.Count;

        public bool TryGetRequired(byte level, out ulong experience)
        {
            experience = 0;
            return level < this.MaxLevel && this.Required.TryGetValue(level, out experience);
        }

        // Adds experience, levelling up while it reaches the next level. At the cap the
        // experience stays at zero, since there is no next level for it to count toward.
        public (byte Level, ulong Experience) Gain(byte level, ulong experience, ulong amount)
        {
            experience = ulong.MaxValue - experience < amount ? ulong.MaxValue : experience + amount;
            while (this.TryGetRequired(level, out var required) && experience >= required)
            {
                experience -= required;
                level++;
            }

            if (level >= this.MaxLevel)
                experience = 0;
            return (level, experience);
        }
    }
}

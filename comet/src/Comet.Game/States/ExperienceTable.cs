namespace Comet.Game.States
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public sealed class ExperienceTable
    {
        private readonly Dictionary<byte, ulong> Required;
        private readonly Dictionary<byte, uint> UpLevTime;

        public const double BallTime = 600;

        private sealed record LevelFile(List<LevelExp> Levels);

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
            var listed = rows.ToList();
            this.Required = listed.Where(r => r.Experience > 0).ToDictionary(r => r.Level, r => r.Experience);
            this.UpLevTime = listed.Where(r => r.UpLevTime > 0).ToDictionary(r => r.Level, r => r.UpLevTime);
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

        // The experience worth this much level-up time (BallTime per EXP ball), counted from
        // the level and experience given, through the levels it would gain
        public ulong ExperienceOfTime(byte level, ulong experience, double time)
        {
            ulong amount = 0;
            while (time > 0 && this.TryGetRequired(level, out var required) &&
                this.UpLevTime.TryGetValue(level, out var upLevTime))
            {
                ulong left = required > experience ? required - experience : 0;
                double leftTime = (double)left / required * upLevTime;
                if (time < leftTime)
                    return amount + (ulong)(time / upLevTime * required);
                time -= leftTime;
                amount += left;
                experience = 0;
                level++;
            }
            return amount;
        }

        public double TimeOfExperience(byte level, ulong amount) =>
            this.TryGetRequired(level, out var required) && this.UpLevTime.TryGetValue(level, out var upLevTime)
                ? (double)amount / required * upLevTime
                : 0;

        // At the cap the experience stays at zero: there is no next level for it to count toward
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

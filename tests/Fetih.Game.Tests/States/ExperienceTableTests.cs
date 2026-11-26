namespace Fetih.Game.Tests.States
{
    using System.Linq;
    using Fetih.Game.States;
    using Xunit;

    public class ExperienceTableTests
    {
        // The cap is level 4
        private static readonly ExperienceTable Table = new ExperienceTable(
            new ulong[] { 10, 20, 30 }.Select((e, i) => new LevelExp { Level = (byte)(i + 1), Experience = e }));

        [Fact]
        public void TheCapIsTheFirstLevelWithoutARow()
        {
            Assert.Equal(4, Table.MaxLevel);
            var gap = new ExperienceTable(new[] {
                new LevelExp { Level = 1, Experience = 5 }, new LevelExp { Level = 3, Experience = 5 } });
            Assert.Equal(2, gap.MaxLevel);
            Assert.Equal(1, new ExperienceTable(new LevelExp[0]).MaxLevel);
        }

        [Theory]
        [InlineData(1, 0, 9, 1, 9)]
        [InlineData(1, 5, 5, 2, 0)]
        [InlineData(1, 0, 35, 3, 5)]
        [InlineData(2, 19, 31, 4, 0)]
        [InlineData(3, 29, 1, 4, 0)]
        [InlineData(1, 0, 1000, 4, 0)]
        [InlineData(4, 0, 50, 4, 0)]
        public void GainLevelsUpWhileTheExperienceReachesTheNextLevel(
            byte level, ulong experience, ulong amount, byte expectedLevel, ulong expectedExperience)
        {
            Assert.Equal((expectedLevel, expectedExperience), Table.Gain(level, experience, amount));
        }

        [Fact]
        public void GainDoesNotOverflow()
        {
            var big = new ExperienceTable(new[] { new LevelExp { Level = 1, Experience = ulong.MaxValue } });
            Assert.Equal((2, 0UL), big.Gain(1, ulong.MaxValue - 1, 10));
        }

        [Fact]
        public void TheShippedLevelFileLoads()
        {
            var experience = new ExperienceTable(ExperienceTable.Load(DataFiles.PathOf("levels.json")));
            Assert.Equal(120, experience.MaxLevel);
            Assert.True(experience.TryGetRequired(1, out var first));
            Assert.Equal(59UL, first);
        }
    }
}

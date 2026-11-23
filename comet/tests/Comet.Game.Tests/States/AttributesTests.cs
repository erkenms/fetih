namespace Comet.Game.Tests.States
{
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Xunit;

    public class AttributesTests
    {
        private static DbCharacter Character(byte job, ushort str, ushort agi, ushort vit, ushort spi) =>
            new DbCharacter(1, "Hero")
            { CurrentClass = job, Strength = str, Agility = agi, Vitality = vit, Spirit = spi };

        [Fact]
        public void HealthMatchesANewCharacter()
        {
            Assert.Equal(318, Attributes.MaxHealth(Character(10, 4, 6, 12, 0), null));
        }

        [Theory]
        [InlineData(10, 1200)]
        [InlineData(11, 1260)]
        [InlineData(15, 1380)]
        [InlineData(25, 1200)]
        public void PromotedTrojansHaveMoreHealth(byte job, int expected)
        {
            Assert.Equal(expected, Attributes.MaxHealth(Character(job, 0, 0, 50, 0), null));
        }

        [Theory]
        [InlineData(100, 50)]
        [InlineData(132, 150)]
        [InlineData(145, 300)]
        public void PromotedTaoistsHaveMoreMana(byte job, int expected)
        {
            Assert.Equal(expected, Attributes.MaxMana(Character(job, 0, 0, 0, 10), null));
        }

        [Fact]
        public void BattlePowerCountsFivePerRebirth()
        {
            var character = new DbCharacter(1, "Hero") { Level = 100, Rebirths = 2 };
            Assert.Equal(110u, Attributes.BattlePower(character));
        }

        [Fact]
        public void ValuesStopAtTheirFieldSize()
        {
            Assert.Equal(ushort.MaxValue, Attributes.MaxHealth(Character(15, 0, 0, 60000, 0), null));
            Assert.Equal(ushort.MaxValue, Attributes.MaxMana(Character(145, 0, 0, 0, 60000), null));
        }
    }
}

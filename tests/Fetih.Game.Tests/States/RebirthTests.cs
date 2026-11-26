namespace Fetih.Game.Tests.States
{
    using Fetih.Game.Database.Models;
    using Fetih.Game.States;
    using Xunit;

    public class RebirthTests
    {
        [Theory]
        [InlineData(1, 15, 120, 30)]
        [InlineData(1, 15, 125, 45)]
        [InlineData(1, 15, 130, 85)]
        [InlineData(1, 15, 135, 85)]   // past 130 counts as 130
        [InlineData(1, 135, 110, 30)]  // water saints from 110, two levels a step
        [InlineData(1, 135, 121, 45)]
        [InlineData(1, 135, 130, 85)]
        [InlineData(2, 25, 120, 52)]
        [InlineData(2, 25, 130, 107)]
        public void LivesRebornHigherGiveMorePoints(byte life, byte currentClass, byte level, int points) =>
            Assert.Equal(points, Rebirth.LifePoints(life, currentClass, level));

        [Theory]
        [InlineData(15, 120)]
        [InlineData(75, 120)]
        [InlineData(145, 120)]
        [InlineData(135, 110)]
        public void WaterSaintsAreRebornTenLevelsSooner(byte currentClass, byte level) =>
            Assert.Equal(level, Rebirth.RequiredLevel(currentClass));

        [Fact]
        public void TheSecondRebirthTakesAnExemptionToken()
        {
            Assert.Equal(Rebirth.CelestialStone, Rebirth.RequiredItem(1));
            Assert.Equal(Rebirth.ExemptionToken, Rebirth.RequiredItem(2));
        }

        [Fact]
        public void OnlyTheRebornPastSeventyReallot()
        {
            Assert.Equal("not_reborn", Rebirth.ReallotUnmet(new DbCharacter(1, "Hero") { Level = 130 }));
            Assert.Equal("level", Rebirth.ReallotUnmet(new DbCharacter(1, "Hero") { Rebirths = 1, Level = 69 }));
            Assert.Null(Rebirth.ReallotUnmet(new DbCharacter(1, "Hero") { Rebirths = 1, Level = 70 }));
        }
    }
}

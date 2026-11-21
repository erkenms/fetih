namespace Comet.Game.Tests.Items
{
    using Comet.Game.Items;
    using Xunit;

    public class UpgradingTests
    {
        [Theory]
        [InlineData(111003u, 111013u)]  // helmets go up by 10
        [InlineData(410305u, 410315u)]  // so do blades
        [InlineData(120003u, 120023u)]  // low necklaces skip a level
        [InlineData(120093u, 120123u)]  // and from level 9 skip two
        [InlineData(421025u, 421045u)]  // backswords have no level 3
        [InlineData(1088000u, null)]    // dragonballs aren't gear
        public void NextLevelsFollowTheClientsItems(uint type, uint? next) =>
            Assert.Equal(next, Upgrading.NextLevelID(type));

        [Theory]
        [InlineData(111013u, 100)]  // low armor always succeeds
        [InlineData(111063u, 40)]
        [InlineData(410125u, 90)]   // weapons by their level
        [InlineData(410229u, 4)]    // a super level-22 weapon: 40% of a tenth
        public void LevelChancesFallWithLevelAndQuality(uint next, double chance) =>
            Assert.Equal(chance, Upgrading.LevelChance(next), 3);
    }
}

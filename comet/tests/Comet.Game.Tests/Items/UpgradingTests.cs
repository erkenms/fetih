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
        [InlineData(111003u, 2)]   // low armor: 80%
        [InlineData(111063u, 3)]   // level 6 armor: 50%
        [InlineData(410125u, 2)]   // a level-12 blade: 85%
        [InlineData(410229u, 11)]  // a super level-22 blade: a fifth of 40% is 8%, raised to 10%
        public void MeteorCostsRiseWithLevelAndQuality(uint type, int meteors) =>
            Assert.Equal(meteors, Upgrading.MeteorCost(type));
    }
}

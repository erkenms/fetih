namespace Comet.Game.Tests.States
{
    using Comet.Game.States;
    using Xunit;

    public class PromotionTests
    {
        [Theory]
        [InlineData(10, 11)]
        [InlineData(24, 25)]
        [InlineData(70, 71)]
        [InlineData(100, 101)]
        [InlineData(132, 133)]
        [InlineData(144, 145)]
        public void EachPromotionIsTheNextRank(byte from, byte to)
        {
            Assert.Equal(to, Promotion.Next(from));
        }

        [Theory]
        [InlineData(15)]
        [InlineData(75)]
        [InlineData(135)]
        [InlineData(145)]
        [InlineData(30)]
        public void MastersAndUnknownClassesGoNoFurther(byte from)
        {
            Assert.Null(Promotion.Next(from));
        }

        [Fact]
        public void TaoistsChooseWaterOrFireAndNothingElse()
        {
            Assert.Null(Promotion.Next(Promotion.Taoist));
            Assert.Null(Promotion.Next(Promotion.Taoist, 152));
            Assert.Equal(Promotion.WaterTaoist, Promotion.Next(Promotion.Taoist, Promotion.WaterTaoist));
            Assert.Equal(Promotion.FireTaoist, Promotion.Next(Promotion.Taoist, Promotion.FireTaoist));
            Assert.Null(Promotion.Next(11, Promotion.FireTaoist));
        }

        [Theory]
        [InlineData(10, 15, 0u)]
        [InlineData(101, 40, 0u)]
        [InlineData(62, 70, Promotion.Emerald)]
        [InlineData(143, 100, Promotion.Meteor)]
        [InlineData(74, 110, Promotion.MoonBox)]
        public void EachRankAsksForALevelAndTheLastThreeForAnItem(byte from, byte level, uint item)
        {
            Assert.Equal(level, Promotion.RequiredLevel(from));
            Assert.Equal(item, Promotion.RequiredItem(from));
        }

        [Fact]
        public void EveryTaoistClassIsOneLine()
        {
            Assert.Equal(10, Promotion.Line(100));
            Assert.Equal(10, Promotion.Line(145));
            Assert.Equal(7, Promotion.Line(73));
        }
    }
}

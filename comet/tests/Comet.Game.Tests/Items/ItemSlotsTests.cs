namespace Comet.Game.Tests.Items
{
    using Comet.Game.Items;
    using Xunit;

    public class ItemSlotsTests
    {
        [Theory]
        [InlineData(410301u, true)]   // blade
        [InlineData(111003u, true)]   // helmet
        [InlineData(300000u, true)]   // steed
        [InlineData(2100045u, true)]  // magical bottle
        [InlineData(1200000u, false)] // praying stone
        [InlineData(1060020u, false)] // city gate scroll
        [InlineData(1000000u, false)] // potion
        public void OnlyGearCanBeWorn(uint type, bool wearable) =>
            Assert.Equal(wearable, ItemSlots.IsWearable(type));
    }
}

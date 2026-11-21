namespace Comet.Game.Tests.Items
{
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Xunit;

    public class DurabilityTests
    {
        [Theory]
        [InlineData(1000u, 500, 1000, 410305u, 375u)]  // half worn, normal: 75%
        [InlineData(1000u, 500, 1000, 410309u, 563u)]  // super: 112.5%, rounded up
        [InlineData(1000u, 1000, 1000, 410305u, 0u)]   // nothing to repair
        [InlineData(1u, 999, 1000, 410305u, 1u)]       // at least 1
        public void RepairsCostTheShareWornByQuality(uint price, ushort durability, ushort max, uint type, uint cost) =>
            Assert.Equal(cost, Durability.RepairCost(price, durability, max, type));

        [Theory]
        [InlineData(410305u, 0, true)]     // a blade with none left
        [InlineData(410305u, 1, false)]
        [InlineData(1050000u, 0, false)]   // an empty quiver isn't broken gear
        [InlineData(300000u, 0, false)]    // nor is a steed
        public void OnlyGearBreaks(uint type, ushort amount, bool broken) =>
            Assert.Equal(broken, Durability.IsBroken(new DbItem { TypeID = type, Amount = amount, AmountLimit = 100 }));
    }
}

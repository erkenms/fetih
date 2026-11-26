namespace Fetih.Game.Tests.Items
{
    using System;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
    using Xunit;

    public class RidingTests
    {
        [Theory]
        [InlineData(300000u)]  // steed
        [InlineData(203009u)]  // crop
        public void RidingGearIsNotAMonsterDrop(uint type) =>
            Assert.False(ItemSlots.IsMonsterDrop(type));

        [Fact]
        public void VigorIsActionTwoWithTheValueAtEight()
        {
            var bytes = MsgData.Vigor(48).Encode();
            Assert.Equal(36, bytes.Length);
            Assert.Equal(1033, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(48u, BitConverter.ToUInt32(bytes, 8));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(4, 3)]
        [InlineData(16, 12)]
        public void JumpsCostThreeVigorPerFourCells(int distance, uint cost) =>
            Assert.Equal(cost, Riding.JumpCost(distance));
    }
}

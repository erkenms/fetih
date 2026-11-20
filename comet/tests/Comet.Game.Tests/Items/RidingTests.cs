namespace Comet.Game.Tests.Items
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class RidingTests
    {
        [Fact]
        public void SteedsGoInSlotTwelveOnly()
        {
            Assert.True(ItemSlots.Fits(300000, ItemPosition.Steed));
            Assert.False(ItemSlots.Fits(300000, ItemPosition.Armor));
            Assert.False(ItemSlots.Fits(200000, ItemPosition.Steed));
            Assert.True(Equipment.IsEquipped(12));
            Assert.False(ItemSlots.IsMonsterDrop(300000));
        }

        [Fact]
        public void ItemAdditionsAreReadByTypeAndPlus()
        {
            var table = ItemAdditionTable.Parse(
                "111000 9 0 0 0 21 0 0 0 0\r\n300000 1 100 20 20 0 20 0 50 5\r\nbad line\r\n");
            Assert.Equal(2, table.Count);
            var steed = table.Find(300000, 1);
            Assert.Equal((100, 50, 5), (steed.Life, steed.Dexterity, steed.Speed));
            Assert.Null(table.Find(300000, 2));
        }

        [Fact]
        public void VigorIsActionTwoWithTheValueAtEight()
        {
            var bytes = MsgData.Vigor(48).Encode();
            Assert.Equal(36, bytes.Length);
            Assert.Equal(1033, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(48u, BitConverter.ToUInt32(bytes, 8));
        }

        [Fact]
        public void TheSteedIsShownToOthers()
        {
            var steed = new DbItem { ItemID = 9, TypeID = 300000, Plus = 6, Position = (byte)ItemPosition.Steed };
            var bytes = new MsgPlayer(new Character(new DbCharacter(7, 1000042, "Hero")), new Equipment(new[] { steed }))
                .Encode();
            Assert.Equal(300000u, BitConverter.ToUInt32(bytes, 72));
            Assert.Equal(6, bytes[141]);
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 76));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(4, 3)]
        [InlineData(16, 12)]
        public void JumpsCostThreeVigorPerFourCells(int distance, uint cost) =>
            Assert.Equal(cost, Riding.JumpCost(distance));
    }
}

namespace Comet.Game.Tests.Items
{
    using System;
    using System.Linq;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Xunit;

    public class InventoryTests
    {
        [Fact]
        public void HoldsFortyItems()
        {
            var inventory = new Inventory(Enumerable.Range(1, 39).Select(i => new DbItem { ItemID = (uint)i }));
            Assert.Equal(1, inventory.FreeSlots);
            Assert.True(inventory.TryAdd(new DbItem { ItemID = 40 }));
            Assert.False(inventory.TryAdd(new DbItem { ItemID = 41 }));
            Assert.Equal(40, inventory.Count);
            Assert.NotNull(inventory.Find(40));
            Assert.Null(inventory.Find(41));
        }

        [Fact]
        public void ItemInfoLayout()
        {
            var item = new DbItem
            {
                ItemID = 7, TypeID = 410301, Amount = 1000, AmountLimit = 1099,
                Position = (byte)ItemPosition.RightHand, Gem1 = 1, Gem2 = 255,
                Plus = 12, Bless = 7, Enchant = 200, Color = 3
            };
            var bytes = new MsgItemInfo(item, MsgItemInfo.ItemAction.Add).Encode();
            Assert.Equal(MsgItemInfo.Size, bytes.Length);
            Assert.Equal(1008, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(410301u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal((1000, 1099), (BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 14)));
            Assert.Equal((1, 4), (BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18)));
            Assert.Equal((1, 255), (bytes[24], bytes[25]));
            Assert.Equal((12, 7, 200), (bytes[33], bytes[34], bytes[36]));
            Assert.Equal(3u, BitConverter.ToUInt32(bytes, 48));
        }
    }
}

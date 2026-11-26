namespace Fetih.Game.Tests.Items
{
    using System;
    using System.Linq;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
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
                Plus = 12, Bless = 7, Enchant = 200, Color = 3, StackAmount = 4
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
            Assert.Equal(4, BitConverter.ToUInt16(bytes, 64));
        }

        [Fact]
        public void BoothAndWarehouseEntriesCarryTheStackAmount()
        {
            var item = new DbItem { ItemID = 7, TypeID = 1000000, StackAmount = 3 };
            Assert.Equal(3, BitConverter.ToUInt16(new MsgItemInfoEx(item, 1, 10, MsgItemInfoEx.ViewType.Silver).Encode(), 72));
            var package = new MsgPackage(8, MsgPackage.PackageAction.Query) { Items = new[] { item } }.Encode();
            Assert.Equal(3, BitConverter.ToUInt16(package, 24 + 44));
        }

        private static DbItem Potion(uint id, ushort stack) => new DbItem { ItemID = id, TypeID = 1000000, StackAmount = stack };

        [Fact]
        public void NewItemsTopUpStacksBeforeTakingSlots()
        {
            var inventory = new Inventory(new[] { Potion(1, 5), Potion(2, 3), new DbItem { ItemID = 3, TypeID = 410301 } });
            Assert.Equal(8, inventory.CountOf(1000000));
            Assert.Equal(0, inventory.SlotsFor(1000000, 2, 5));
            Assert.Equal(1, inventory.SlotsFor(1000000, 3, 5));
            Assert.Equal(2, inventory.SlotsFor(1000000, 8, 5));
            Assert.Equal(4, inventory.SlotsFor(410301, 4, 1));

            Assert.Same(inventory.Find(2), inventory.StackOne(1000000, 5));
            Assert.Equal(4, inventory.Find(2).StackAmount);
            inventory.StackOne(1000000, 5);
            Assert.Null(inventory.StackOne(1000000, 5));
            Assert.Null(inventory.StackOne(410301, 1));
        }

        [Fact]
        public void TakingCountsWhatStacksHold()
        {
            var inventory = new Inventory(new[] { Potion(1, 2), Potion(2, 5) });
            Assert.Null(inventory.Take(1000000, 8));
            var taken = inventory.Take(1000000, 4);
            Assert.Equal(new uint[] { 1, 2 }, taken.Select(i => i.ItemID));
            Assert.Null(inventory.Find(1));
            Assert.Equal(3, inventory.Find(2).StackAmount);

            var stack = inventory.Find(2);
            Assert.True(inventory.TakeOne(stack));
            Assert.Equal(2, stack.StackAmount);
            Assert.True(inventory.TakeOne(stack));
            Assert.True(inventory.TakeOne(stack));
            Assert.False(inventory.Contains(stack));
            Assert.False(inventory.TakeOne(stack));
        }

        [Fact]
        public void SplittingLeavesAtLeastOneAndNeedsASlot()
        {
            var stack = Potion(1, 5);
            var inventory = new Inventory(new[] { stack });
            Assert.False(inventory.Split(stack, Potion(2, 0), 5));
            Assert.False(inventory.Split(stack, Potion(2, 0), 0));
            var part = Potion(2, 0);
            Assert.True(inventory.Split(stack, part, 2));
            Assert.Equal((3, 2), (stack.StackAmount, part.StackAmount));
            Assert.Same(part, inventory.Find(2));

            var full = new Inventory(Enumerable.Range(1, Inventory.Capacity).Select(i => Potion((uint)i, 5)));
            Assert.False(full.Split(full.Find(1), Potion(99, 0), 1));
        }

        [Fact]
        public void NewItemsTakeTheirTypesDurability()
        {
            var type = new ItemType { ID = 410301, Amount = 1099, AmountLimit = 1099, Gem1 = 255 };
            var item = ItemActions.New(1000001, type);
            Assert.Equal((1000001u, 410301u, (byte)0), (item.CharacterID, item.TypeID, item.Position));
            Assert.Equal((1099, 1099, 255), (item.Amount, item.AmountLimit, item.Gem1));
        }
    }
}

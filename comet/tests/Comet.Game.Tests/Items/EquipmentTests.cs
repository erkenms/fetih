namespace Comet.Game.Tests.Items
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class EquipmentTests
    {
        [Theory]
        [InlineData(111003u, ItemPosition.Headwear)]
        [InlineData(117003u, ItemPosition.Headwear)]
        [InlineData(141003u, ItemPosition.Headwear)]
        [InlineData(120001u, ItemPosition.Necklace)]
        [InlineData(121003u, ItemPosition.Necklace)]
        [InlineData(135303u, ItemPosition.Armor)]
        [InlineData(410301u, ItemPosition.RightHand)]
        [InlineData(410301u, ItemPosition.LeftHand)]
        [InlineData(601003u, ItemPosition.LeftHand)]
        [InlineData(510301u, ItemPosition.RightHand)]
        [InlineData(900000u, ItemPosition.LeftHand)]
        [InlineData(1050000u, ItemPosition.LeftHand)]
        [InlineData(150003u, ItemPosition.Ring)]
        [InlineData(160013u, ItemPosition.Boots)]
        [InlineData(181305u, ItemPosition.Garment)]
        public void TypesFitTheirSlots(uint type, ItemPosition position) =>
            Assert.True(ItemSlots.Fits(type, position));

        [Theory]
        [InlineData(510301u, ItemPosition.LeftHand)]
        [InlineData(900000u, ItemPosition.RightHand)]
        [InlineData(1050000u, ItemPosition.RightHand)]
        [InlineData(111003u, ItemPosition.Armor)]
        [InlineData(1000000u, ItemPosition.RightHand)]
        [InlineData(1000000u, ItemPosition.Inventory)]
        [InlineData(200001u, ItemPosition.Garment)]
        public void TypesDontFitOtherSlots(uint type, ItemPosition position) =>
            Assert.False(ItemSlots.Fits(type, position));

        [Theory]
        [InlineData(10, 11, false)]  // an intern trojan, a trojan item
        [InlineData(15, 11, true)]
        [InlineData(21, 21, true)]
        [InlineData(25, 11, false)]  // a warrior, a trojan item
        [InlineData(100, 190, true)] // any taoist, a taoist item
        [InlineData(142, 190, true)]
        [InlineData(45, 190, false)]
        public void ProfessionsMatchLineAndRank(byte currentClass, byte required, bool fits) =>
            Assert.Equal(fits, Requirements.ProfessionFits(currentClass, required));

        [Fact]
        public void RequirementsNameTheFirstUnmetOne()
        {
            var character = new DbCharacter(1, 1, "Hero") { Level = 10, Mesh = 1003, CurrentClass = 21, Strength = 20 };
            Assert.Null(Requirements.Unmet(character, new ItemType { RequiredLevel = 10, RequiredGender = 1 }));
            Assert.Equal("level", Requirements.Unmet(character, new ItemType { RequiredLevel = 15 }));
            Assert.Equal("gender", Requirements.Unmet(character, new ItemType { RequiredGender = 2 }));
            Assert.Equal("profession", Requirements.Unmet(character, new ItemType { RequiredProfession = 41 }));
            Assert.Equal("attributes", Requirements.Unmet(character, new ItemType { RequiredStrength = 21 }));
        }

        [Fact]
        public void EquippedSlotsAreOneToNine()
        {
            Assert.False(Equipment.IsEquipped(0));
            Assert.True(Equipment.IsEquipped(1));
            Assert.True(Equipment.IsEquipped(9));
            Assert.False(Equipment.IsEquipped(10));
        }

        [Fact]
        public void PlayersShowWhatTheyWear()
        {
            var character = new Character(new DbCharacter(1, 1000001, "Hero") { Mesh = 1003, Level = 1 });
            var equipment = new Equipment(new[]
                {
                    new DbItem { ItemID = 1, TypeID = 111003, Position = 1, Color = 5 },
                    new DbItem { ItemID = 2, TypeID = 130003, Position = 3, Color = 3 },
                    new DbItem { ItemID = 3, TypeID = 410301, Position = 4 },
                    new DbItem { ItemID = 4, TypeID = 900000, Position = 5, Color = 7 },
                    new DbItem { ItemID = 5, TypeID = 181305, Position = 9 },
                });
            var bytes = new MsgPlayer(character, equipment).Encode();
            Assert.Equal((111003u, 181305u, 130003u), (BitConverter.ToUInt32(bytes, 44),
                BitConverter.ToUInt32(bytes, 48), BitConverter.ToUInt32(bytes, 52)));
            Assert.Equal((410301u, 900000u), (BitConverter.ToUInt32(bytes, 56), BitConverter.ToUInt32(bytes, 60)));
            Assert.Equal((3, 7, 5), (BitConverter.ToUInt16(bytes, 131), BitConverter.ToUInt16(bytes, 133),
                BitConverter.ToUInt16(bytes, 135)));
            Assert.Equal("Hero", System.Text.Encoding.Latin1.GetString(bytes, 228, 4));
        }

        [Fact]
        public void DisplayGearsListsItemIDsBySlot()
        {
            var equipment = new Equipment(new[]
            {
                new DbItem { ItemID = 11, Position = 1 },
                new DbItem { ItemID = 14, Position = 4 },
                new DbItem { ItemID = 19, Position = 9 },
            });
            var bytes = MsgItem.DisplayGears(1000001, equipment).Encode();
            Assert.Equal(MsgItem.Size, bytes.Length);
            Assert.Equal((1000001u, 255u, 46u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8),
                BitConverter.ToUInt32(bytes, 12)));
            Assert.Equal((11u, 0u, 14u, 19u), (BitConverter.ToUInt32(bytes, 32), BitConverter.ToUInt32(bytes, 36),
                BitConverter.ToUInt32(bytes, 44), BitConverter.ToUInt32(bytes, 64)));
        }

        [Fact]
        public void EquipRepliesCarryTheItemAndSlot()
        {
            var bytes = MsgItem.Equipped(7, ItemPosition.RightHand).Encode();
            Assert.Equal(MsgItem.Size, bytes.Length);
            Assert.Equal((7u, 4u, 5u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8),
                BitConverter.ToUInt32(bytes, 12)));
            Assert.Equal(6u, BitConverter.ToUInt32(MsgItem.Unequipped(7, ItemPosition.RightHand).Encode(), 12));
        }
    }
}

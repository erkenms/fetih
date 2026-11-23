namespace Comet.Game.Tests.Items
{
    using System;
    using System.Linq;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Magic;
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
        [InlineData(300000u, ItemPosition.Steed)]
        [InlineData(203009u, ItemPosition.Crop)]
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
        [InlineData(300000u, ItemPosition.Armor)]
        [InlineData(300000u, ItemPosition.Crop)]
        [InlineData(200000u, ItemPosition.Steed)]
        [InlineData(203009u, ItemPosition.Steed)]
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

            var blade = new ItemType { ID = 410301, RequiredWeaponSkill = 5 };
            var skills = new Skills();
            Assert.Equal("proficiency", Requirements.Unmet(character, blade, skills));
            skills.SetProficiency(410, new SkillLevel(4, 0));
            Assert.Equal("proficiency", Requirements.Unmet(character, blade, skills));
            skills.SetProficiency(410, new SkillLevel(5, 0));
            Assert.Null(Requirements.Unmet(character, blade, skills));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(9, true)]
        [InlineData(10, false)]
        [InlineData(11, false)]
        [InlineData(12, true)]   // steed
        [InlineData(18, true)]   // crop
        [InlineData(19, false)]
        public void WornSlotsAreOneToNineTheSteedAndTheCrop(byte position, bool equipped) =>
            Assert.Equal(equipped, Equipment.IsEquipped(position));

        [Fact]
        public void DisplayGearsListsItemIDsBySlot()
        {
            var equipment = new Equipment(new[]
            {
                new DbItem { ItemID = 11, Position = 1 },
                new DbItem { ItemID = 14, Position = 4 },
                new DbItem { ItemID = 19, Position = 9 },
                new DbItem { ItemID = 20, Position = 12 },
                new DbItem { ItemID = 21, Position = 18 },
            });
            var bytes = MsgItem.DisplayGears(1000001, equipment).Encode();
            Assert.Equal(MsgItem.Size, bytes.Length);
            Assert.Equal((1000001u, 255u, 46u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8),
                BitConverter.ToUInt32(bytes, 12)));
            Assert.Equal((11u, 0u, 14u, 19u), (BitConverter.ToUInt32(bytes, 32), BitConverter.ToUInt32(bytes, 36),
                BitConverter.ToUInt32(bytes, 44), BitConverter.ToUInt32(bytes, 64)));
            // The crop after the accessories and steed armor; the steed has no place
            Assert.Equal(21u, BitConverter.ToUInt32(bytes, 80));
            Assert.DoesNotContain(20u, Enumerable.Range(0, 14).Select(i => BitConverter.ToUInt32(bytes, 32 + 4 * i)));
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

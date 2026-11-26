namespace Fetih.Game.Tests.Pk
{
    using System;
    using System.Text;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
    using Fetih.Game.Pk;
    using Xunit;

    public class DetainingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void DaysElapsedCoversSevenDayTermThenExpiresAtEight()
        {
            Assert.Equal(0u, Detaining.DaysElapsed(Now, Now));
            Assert.Equal(3u, Detaining.DaysElapsed(Now.AddDays(-3), Now));
            Assert.Equal(6u, Detaining.DaysElapsed(Now.AddDays(-6).AddHours(-23), Now));
            Assert.False(Detaining.IsExpired(new DbDetainedItem { DetainedAt = Now.AddDays(-6).AddHours(-23) }, Now));

            Assert.True(Detaining.IsExpired(new DbDetainedItem { DetainedAt = Now.AddDays(-7) }, Now));
            Assert.Equal(8u, Detaining.DaysElapsed(Now.AddDays(-7), Now));
            Assert.Equal(11u, Detaining.DaysElapsed(Now.AddDays(-10), Now));
        }

        [Fact]
        public void RedeemCostScalesWithQualityPlusSocketsAndBless()
        {
            var plain = new DbItem { TypeID = 410003 };
            var superPlus = new DbItem { TypeID = 410009, Plus = 3, Gem1 = 13, Gem2 = 13, Bless = 5 };
            Assert.Equal(10u, Detaining.RedeemCost(plain));
            Assert.Equal(50u + 50u + 50u + 150u + 50u, Detaining.RedeemCost(superPlus));
        }

        [Fact]
        public void OnlyEquippedNonArrowGearIsDetainable()
        {
            Assert.True(Detaining.IsDetainable(new DbItem { TypeID = 410003, Position = (byte)ItemPosition.RightHand }));
            Assert.True(Detaining.IsDetainable(new DbItem { TypeID = 130003, Position = (byte)ItemPosition.Armor }));
            Assert.False(Detaining.IsDetainable(new DbItem { TypeID = 410003, Position = (byte)ItemPosition.Inventory }));
            Assert.False(Detaining.IsDetainable(new DbItem { TypeID = 1050000, Position = (byte)ItemPosition.LeftHand }));
        }

        [Fact]
        public void MsgDetainItemInfoEncodesClientLayout()
        {
            var entry = new DbDetainedItem
            {
                DetainID = 42,
                ItemID = 9001,
                TargetID = 1001,
                TargetName = "RedHero",
                HunterID = 2002,
                HunterName = "Hunter",
                Cost = 150,
                DetainedAt = Now,
            };
            var item = new DbItem
            {
                ItemID = 9001,
                TypeID = 410009,
                Position = (byte)ItemPosition.RightHand,
                Amount = 100,
                AmountLimit = 200,
                Gem1 = 13,
                Gem2 = 255,
                Plus = 4,
                Bless = 3,
                Enchant = 100,
                PlusProgress = 120,
                Locked = true,
                Color = 5,
            };

            var bytes = MsgDetainItemInfo.FromItem(entry, item, MsgDetainItemInfo.DetainMode.DetainPage, days: 2).Encode();
            Assert.Equal(MsgDetainItemInfo.Size, bytes.Length);
            Assert.Equal((ushort)1034, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(42u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(9001u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(410009u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal((ushort)100, BitConverter.ToUInt16(bytes, 16));
            Assert.Equal((ushort)200, BitConverter.ToUInt16(bytes, 18));
            Assert.Equal((byte)0, bytes[20]);
            Assert.Equal((byte)ItemPosition.RightHand, bytes[21]);
            Assert.Equal((byte)13, bytes[28]);
            Assert.Equal((byte)255, bytes[29]);
            Assert.Equal((byte)4, bytes[36]);
            Assert.Equal((byte)3, bytes[37]);
            Assert.Equal((byte)100, bytes[40]);
            Assert.Equal(120u, BitConverter.ToUInt32(bytes, 44));
            Assert.Equal((ushort)1, BitConverter.ToUInt16(bytes, 50));
            Assert.Equal((ushort)5, BitConverter.ToUInt16(bytes, 52));
            Assert.Equal(1001u, BitConverter.ToUInt32(bytes, 56));
            Assert.Equal("RedHero", Encoding.ASCII.GetString(bytes, 60, 7));
            Assert.Equal(2002u, BitConverter.ToUInt32(bytes, 76));
            Assert.Equal("Hunter", Encoding.ASCII.GetString(bytes, 80, 6));
            Assert.Equal(0, BitConverter.ToInt32(bytes, 100));
            Assert.Equal(150u, BitConverter.ToUInt32(bytes, 104));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 108));
        }

        [Fact]
        public void MsgDetainItemUpdateEncodesEntries()
        {
            var msg = new MsgDetainItemUpdate(MsgDetainItemUpdate.RewardPage);
            msg.Entries.Add(new MsgDetainItemUpdate.Entry(42, 8, Keep: true));
            msg.Entries.Add(new MsgDetainItemUpdate.Entry(43, 0, Keep: false));

            var bytes = msg.Encode();
            Assert.Equal(12 + 24, bytes.Length);
            Assert.Equal((ushort)1035, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(2, BitConverter.ToInt32(bytes, 8));
            Assert.Equal(42u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(8u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal((byte)1, bytes[20]);
            Assert.Equal(43u, BitConverter.ToUInt32(bytes, 24));
            Assert.Equal((byte)0, bytes[32]);
        }
    }
}

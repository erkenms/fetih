namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Xunit;

    public class LockingPacketsTests
    {
        [Fact]
        public void EquipLockHasItemModeFlagAndValue()
        {
            var bytes = new MsgEquipLock(1000010, MsgEquipLock.LockMode.Unlock) { Flag = 3, Value = 20261126 }.Encode();
            Assert.Equal(16, bytes.Length);
            Assert.Equal(2048, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000010u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal((1, 3), (bytes[8], bytes[9]));
            Assert.Equal(20261126u, BitConverter.ToUInt32(bytes, 12));

            var msg = new MsgEquipLock();
            msg.Decode(Comet.Client.EquipLockMessage.Build(1000010, Comet.Client.EquipLockMessage.Unlock));
            Assert.Equal((1000010u, MsgEquipLock.LockMode.Unlock), (msg.ItemID, msg.Mode));
        }

        [Fact]
        public void SecondPasswordHasRequestThenBothPasswords()
        {
            var msg = new Msg2ndPsw();
            msg.Decode(Convert.FromHexString("1000D508" + "03000000" + "D2040000" + "2E160000"));
            Assert.Equal((Msg2ndPsw.PasswordRequest.Set, 1234u, 5678u), (msg.Request, msg.Password, msg.NewPassword));
            var bytes = new Msg2ndPsw(Msg2ndPsw.PasswordRequest.Correct, Msg2ndPsw.HasPassword).Encode();
            Assert.Equal(16, bytes.Length);
            Assert.Equal(2261, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((5u, 1u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8)));
        }

        [Fact]
        public void TradeBuddyHasIdActionOnlineMinutesAndName()
        {
            var bytes = new MsgTradeBuddy(MsgTradeBuddy.PartnerAction.Add, 1000002, "Altay", true, 4320).Encode();
            Assert.Equal(32, bytes.Length);
            Assert.Equal(2046, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal((5, 1), (bytes[8], bytes[9]));
            Assert.Equal(4320, BitConverter.ToInt32(bytes, 10));
            Assert.Equal("Altay", Encoding.ASCII.GetString(bytes, 16, 16).TrimEnd('\0'));
        }

        [Fact]
        public void TradeBuddyInfoEndsWithTheSpouse()
        {
            var bytes = new MsgTradeBuddyInfo { ID = 1000002, Level = 70, Class = 15, GuildID = 9 }.Encode();
            Assert.Equal(42, bytes.Length);
            Assert.Equal(2047, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((70, 15), (bytes[12], bytes[13]));
            Assert.Equal(9u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal("None", Encoding.ASCII.GetString(bytes, 26, 16).TrimEnd('\0'));
        }

        [Fact]
        public void ItemInfoAndWarehouseShowTheLock()
        {
            var item = new DbItem { ItemID = 1, TypeID = 410301, Locked = true };
            Assert.Equal(1, new MsgItemInfo(item, MsgItemInfo.ItemAction.Update).Encode()[46]);
            Assert.Equal(1, new MsgPackage(8, MsgPackage.PackageAction.Query) { Items = new[] { item } }.Encode()[24 + 26]);
            item.Locked = false;
            Assert.Equal(0, new MsgItemInfo(item, MsgItemInfo.ItemAction.Update).Encode()[46]);
        }

        [Fact]
        public void AnItemIsLockedUntilItsUnlockIsDue()
        {
            var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(Locking.IsLocked(new DbItem(), now));
            Assert.True(Locking.IsLocked(new DbItem { Locked = true }, now));
            Assert.True(Locking.IsLocked(new DbItem { Locked = true, UnlocksAt = now.AddSeconds(1) }, now));
            Assert.False(Locking.IsLocked(new DbItem { Locked = true, UnlocksAt = now }, now));
        }

        [Fact]
        public void PartnersAreFormalAfterThreeDays()
        {
            var since = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
            var partner = new TradePartner("Altay", since);
            Assert.False(partner.IsFormal(since.AddDays(3).AddSeconds(-1)));
            Assert.Equal(4320, partner.TrialMinutes(since));
            Assert.Equal(1, partner.TrialMinutes(since.AddDays(3).AddSeconds(-1)));
            Assert.True(partner.IsFormal(since.AddDays(3)));
            Assert.Equal(0, partner.TrialMinutes(since.AddDays(4)));
        }
    }
}

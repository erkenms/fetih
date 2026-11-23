namespace Comet.Game.Tests.Packets
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Xunit;

    public class MsgDataArrayTests
    {
        // As the 5619 client's compose window sends it: 16 bytes, action 0, two values
        [Fact]
        public void ComposeHasTheMainItemThenTheMinor()
        {
            var bytes = new byte[16];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)16);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)2036);
            bytes[4] = 0;
            bytes[5] = 2;
            BitConverter.TryWriteBytes(bytes.AsSpan(8), 1001u);
            BitConverter.TryWriteBytes(bytes.AsSpan(12), 1002u);
            var msg = new MsgDataArray();
            msg.Decode(bytes);
            Assert.Equal(MsgDataArray.DataArrayAction.Compose, msg.Action);
            Assert.Equal(new uint[] { 1001, 1002 }, msg.Values);
        }

        [Fact]
        public void ACountPastTheEndReadsWhatIsThere()
        {
            var bytes = new byte[12];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)12);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)2036);
            bytes[5] = 5;
            var msg = new MsgDataArray();
            msg.Decode(bytes);
            Assert.Single(msg.Values);
        }

        // As the 5619 client sent it on 2026-11-21, from window 2: item 10062 with item 24
        [Fact]
        public void FuseHasTheMainItemThenTheMinors()
        {
            var msg = new MsgFuse();
            msg.Decode(Convert.FromHexString("100004044E2700000000010018000000"));
            Assert.Equal(10062u, msg.MainID);
            Assert.Equal(new uint[] { 24 }, msg.MinorIDs);
        }

        [Fact]
        public void ItemInfoHasThePlusProgressAt52()
        {
            var bytes = new MsgItemInfo(new DbItem { ItemID = 1, Plus = 3, Enchant = 200, PlusProgress = 150 },
                MsgItemInfo.ItemAction.Update).Encode();
            Assert.Equal(3, bytes[33]);
            Assert.Equal(200, bytes[36]);
            Assert.Equal(150u, BitConverter.ToUInt32(bytes, 52));
        }
    }
}

namespace Comet.Game.Tests.Packets
{
    using System;
    using Comet.Game.Packets;
    using Xunit;
    using static Comet.Game.Packets.MsgUserAttrib;

    public class MsgUserAttribTests
    {
        [Fact]
        public void EachAttributeTakesTwentyBytes()
        {
            var bytes = new MsgUserAttrib(1000042)
                .Append(AttributeType.Level, 3)
                .Append(AttributeType.Experience, 0x0102030405060708)
                .Encode();
            Assert.Equal(10017, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000042u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(12u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(3UL, BitConverter.ToUInt64(bytes, 16));
            Assert.Equal(5u, BitConverter.ToUInt32(bytes, 32));
            Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 36));
            Assert.Equal(12 + 2 * 20, bytes.Length);
        }

        [Fact]
        public void AllotReadsOneBytePerAttribute()
        {
            var msg = new MsgAllot();
            msg.Decode(new byte[] { 8, 0, 0x00, 0x04, 1, 2, 3, 4 });
            Assert.Equal((1, 2, 3, 4), (msg.Strength, msg.Agility, msg.Vitality, msg.Spirit));
        }
    }
}

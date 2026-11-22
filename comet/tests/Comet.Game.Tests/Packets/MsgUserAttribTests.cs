namespace Comet.Game.Tests.Packets
{
    using System;
    using Comet.Game.Packets;
    using Xunit;
    using static Comet.Game.Packets.MsgUserAttrib;

    public class MsgUserAttribTests
    {
        [Fact]
        public void EachAttributeTakesTwentyFourBytes()
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
            Assert.Equal(5u, BitConverter.ToUInt32(bytes, 36));
            Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 40));
            Assert.Equal(12 + 2 * 24, bytes.Length);
        }

        [Fact]
        public void AllotReadsFourBytesPerAttributeFromEight()
        {
            // As the 5619 client sent it: 3 points to spirit
            var msg = new MsgAllot();
            msg.Decode(Convert.FromHexString("180000040000000000000000000000000000000003000000"));
            Assert.Equal((0u, 0u, 0u, 3u), (msg.Strength, msg.Agility, msg.Vitality, msg.Spirit));
            msg.Decode(Comet.Client.AllotMessage.Build(1, 2, 3, 4));
            Assert.Equal((1u, 2u, 3u, 4u), (msg.Strength, msg.Agility, msg.Vitality, msg.Spirit));
        }
    }
}

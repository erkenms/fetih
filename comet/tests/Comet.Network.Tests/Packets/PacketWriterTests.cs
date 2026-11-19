namespace Comet.Network.Security.Tests
{
    using System.Collections.Generic;
    using Comet.Network.Packets;
    using Org.BouncyCastle.Utilities.Encoders;
    using Xunit;

    public class PacketWriterTests
    {
        const string ExpectedOutput = 
            "0000e90301020003000000040000000000000004746573746" +
            "36f6d657400000000000000020568656c6c6f05776f726c64";

        [Fact]
        public void PacketWriteTest()
        {
            PacketWriter packet = new PacketWriter();
            packet.Write((ushort)PacketType.MsgRegister);
            packet.Write((byte)1);
            packet.Write((ushort)2);
            packet.Write((uint)3);
            packet.Write((ulong)4);
            packet.Write("test");
            packet.Write("comet", 12);
            packet.Write(new List<string> { "hello", "world" });
            Assert.Equal(ExpectedOutput, Hex.ToHexString(packet.ToArray()));
        }

        [Fact]
        public void StringListLengthsAreOneByteAndTextKeepsItsBytes()
        {
            var packet = new PacketWriter();
            packet.Write(new List<string> { new string('\u00E7', 200), new string('a', 300) });
            var bytes = packet.ToArray();
            Assert.Equal(2, bytes[2]);
            Assert.Equal(200, bytes[3]);
            Assert.Equal(0xE7, bytes[4]);
            Assert.Equal(255, bytes[4 + 200]);
            Assert.Equal(2 + 1 + 201 + 256, bytes.Length);
        }
    }
}

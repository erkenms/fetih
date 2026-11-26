namespace Fetih.Network.Security.Tests
{
    using System;
    using System.Collections.Generic;
    using Fetih.Network.Packets;
    using Xunit;

    public class PacketWriterTests
    {
        const string ExpectedOutput =
            "0000" + "e903" + "01" + "0200" + "03000000" + "0400000000000000" +
            "666574696800000000000000" + "020568656c6c6f05776f726c64";

        [Fact]
        public void PacketWriteTest()
        {
            PacketWriter packet = new PacketWriter();
            packet.Write((ushort)PacketType.MsgRegister);
            packet.Write((byte)1);
            packet.Write((ushort)2);
            packet.Write((uint)3);
            packet.Write((ulong)4);
            packet.Write("fetih", 12);
            packet.Write(new List<string> { "hello", "world" });
            Assert.Equal(ExpectedOutput, Convert.ToHexString(packet.ToArray()).ToLowerInvariant());
        }

        [Fact]
        public void FixedLengthStringLongerThanItsFieldThrows()
        {
            var packet = new PacketWriter();
            Assert.Throws<ArgumentException>(() => packet.Write("fetih", 4));
        }

        [Fact]
        public void StringListLengthsAreOneByteAndTextKeepsItsBytes()
        {
            var packet = new PacketWriter();
            packet.Write(new List<string> { new string('ç', 200), new string('a', 300) });
            var bytes = packet.ToArray();
            Assert.Equal(2, bytes[2]);
            Assert.Equal(200, bytes[3]);
            Assert.Equal(0xE7, bytes[4]);
            Assert.Equal(255, bytes[4 + 200]);
            Assert.Equal(2 + 1 + 201 + 256, bytes.Length);
        }
    }
}

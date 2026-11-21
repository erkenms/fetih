namespace Comet.Network.Security.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Comet.Network.Packets;
    using Xunit;

    public class PacketReaderTests
    {
        const string InputBuffer =
            "0000" + "e903" + "01" + "0200" + "03000000" + "0400000000000000" +
            "636f6d657400000000000000" + "020568656c6c6f05776f726c64";

        [Fact]
        public void PacketReadTest()
        {
            PacketReader packet = new PacketReader(Convert.FromHexString(InputBuffer));
            Assert.Equal((ushort)0, packet.ReadUInt16());
            Assert.Equal((ushort)PacketType.MsgRegister, packet.ReadUInt16());
            Assert.Equal((byte)1, packet.ReadByte());
            packet.BaseStream.Seek(-1, SeekOrigin.Current);
            Assert.True(packet.ReadBoolean());
            Assert.Equal((ushort)2, packet.ReadUInt16());
            Assert.Equal((uint)3, packet.ReadUInt32());
            Assert.Equal((ulong)4, packet.ReadUInt64());
            Assert.Equal("comet", packet.ReadString(12));
            Assert.Equal(new List<string>{ "hello", "world" }, packet.ReadStrings());
        }

        [Fact]
        public void StringListLengthsAreOneByte()
        {
            var bytes = new byte[] { 1, 200 }.Concat(Enumerable.Repeat((byte)0xE7, 200)).ToArray();
            var strings = new PacketReader(bytes).ReadStrings();
            Assert.Equal(new string('ç', 200), strings[0]);
        }

        [Theory]
        [InlineData("bytes", "010203")]
        [InlineData("string", "636f6d6574")]
        [InlineData("strings", "02")]
        [InlineData("strings", "0205")]
        [InlineData("strings", "020568656c6c6f05776f72")]
        public void TruncatedReadsThrow(string read, string hex)
        {
            var reader = new PacketReader(Convert.FromHexString(hex));
            Action action = read switch
            {
                "bytes" => () => reader.ReadBytes(4),
                "string" => () => reader.ReadString(12),
                _ => () => reader.ReadStrings(),
            };
            Assert.Throws<EndOfStreamException>(action);
        }
    }
}

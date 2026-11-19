namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Packets;
    using Xunit;

    public class MsgRegisterTests
    {
        private static byte[] ClientPacket(uint token)
        {
            var bytes = new byte[124];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)124);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)1001);
            Encoding.ASCII.GetBytes("NewHero").CopyTo(bytes, 24);
            BitConverter.TryWriteBytes(bytes.AsSpan(72), (ushort)2001);
            BitConverter.TryWriteBytes(bytes.AsSpan(74), (ushort)60);
            BitConverter.TryWriteBytes(bytes.AsSpan(76), token);
            Encoding.ASCII.GetBytes("00.1C.42.AA.BB.CC").CopyTo(bytes, 80);
            return bytes;
        }

        [Fact]
        public void DecodesClientLayout()
        {
            var msg = new MsgRegister();
            msg.Decode(ClientPacket(206152920));
            Assert.Equal("NewHero", msg.CharacterName);
            Assert.Equal(2001, msg.Mesh);
            Assert.Equal(60, msg.Class);
            Assert.Equal(206152920u, msg.Token);
            Assert.Equal("00.1C.42.AA.BB.CC", msg.MacAddress);
        }

        [Fact]
        public void KeepsBytesOutsideAsciiSoTheyCanBeRejected()
        {
            var bytes = ClientPacket(1);
            new byte[] { 0xC7, 0x61, 0xF0, 0x72, 0xFD, 0 }.CopyTo(bytes, 24);
            var msg = new MsgRegister();
            msg.Decode(bytes);
            Assert.Equal("\u00C7a\u00F0r\u00FD", msg.CharacterName);
        }

        [Fact]
        public void NameWithoutTerminatorIsSixteenCharacters()
        {
            var bytes = ClientPacket(1);
            Encoding.ASCII.GetBytes("SixteenCharsLong").CopyTo(bytes, 24);
            var msg = new MsgRegister();
            msg.Decode(bytes);
            Assert.Equal("SixteenCharsLong", msg.CharacterName);
        }
    }
}

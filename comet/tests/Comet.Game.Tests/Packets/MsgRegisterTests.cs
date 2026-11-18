namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Packets;
    using Xunit;

    /// <summary>
    /// Checks MsgRegister against the 124 byte layout the 5619 client sends (seen in VM:
    /// zero at 4 and the MsgConnect token at 76).
    /// </summary>
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
    }
}

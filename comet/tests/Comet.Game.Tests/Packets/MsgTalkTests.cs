namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Drawing;
    using System.Text;
    using Comet.Game.Packets;
    using Xunit;
    using static Comet.Game.Packets.MsgTalk;

    public class MsgTalkTests
    {
        [Fact]
        public void EncodesStringsAfterTheHeader()
        {
            var text = new string('a', 200);
            var bytes = new MsgTalk(7, TalkChannel.Whisper, Color.White, "Bob", "Alice", text)
                { SenderMesh = 1003, RecipientMesh = 2001 }.Encode();
            Assert.Equal(1004, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(0x00FFFFFF, BitConverter.ToInt32(bytes, 4));
            Assert.Equal(2001, BitConverter.ToUInt16(bytes, 8));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(2001u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(1003u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(4, bytes[24]);
            Assert.Equal(5, bytes[25]);
            Assert.Equal("Alice", Encoding.ASCII.GetString(bytes, 26, 5));
            Assert.Equal(200, bytes[26 + 5 + 1 + 3 + 1]);
        }

        [Fact]
        public void DecodeReadsWhatEncodeWrote()
        {
            var text = "Çok güzel! " + new string('z', 180);
            var bytes = new MsgTalk(7, TalkChannel.World, Color.White, "ALLUSERS", "Alice", text).Encode();
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)bytes.Length);
            var msg = new MsgTalk();
            msg.Decode(bytes);
            Assert.Equal(TalkChannel.World, msg.Channel);
            Assert.Equal(("Alice", "ALLUSERS", text), (msg.SenderName, msg.RecipientName, msg.Message));
        }
    }
}

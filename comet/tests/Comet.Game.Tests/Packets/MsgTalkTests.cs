namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Drawing;
    using System.Text;
    using Comet.Game.Chat;
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

        [Theory]
        [InlineData(true, 10, true)]
        [InlineData(true, 100, true)]
        [InlineData(true, 132, true)]
        [InlineData(true, 142, true)]
        [InlineData(false, 132, true)]
        [InlineData(false, 133, true)]
        [InlineData(false, 134, true)]
        [InlineData(false, 135, true)]
        [InlineData(false, 100, false)]
        [InlineData(false, 101, false)]
        [InlineData(false, 142, false)]
        [InlineData(false, 145, false)]
        [InlineData(false, 10, false)]
        [InlineData(false, 25, false)]
        public void OnlyGhostsAndWaterTaoistsHearGhostChat(bool dead, byte currentClass, bool expected) =>
            Assert.Equal(expected, Talk.CanHearGhost(dead, currentClass));
    }
}

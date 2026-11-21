namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class MsgNameTests
    {
        [Fact]
        public void DecodesTheClientsWhisperQuery()
        {
            var msg = new MsgName();
            msg.Decode(Convert.FromHexString("1200F703000000001A0105416C7461790000"));
            Assert.Equal(MsgName.NameAction.WhisperDetails, msg.Action);
            Assert.Equal(new[] { "Altay" }, msg.Strings);
        }

        [Fact]
        public void EncodesTheReplyStrings()
        {
            var msg = new MsgName { Action = MsgName.NameAction.WhisperDetails };
            msg.Strings.Add("Altay");
            msg.Strings.Add("1000001 70 70 #Mogollar #Adamlar None 0 1");
            var bytes = msg.Encode();
            Assert.Equal(1015, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(26, bytes[8]);
            Assert.Equal(2, bytes[9]);
            Assert.Equal(5, bytes[10]);
            Assert.Equal("Altay", Encoding.ASCII.GetString(bytes, 11, 5));
            Assert.Equal(41, bytes[16]);
            Assert.Equal("1000001 70 70 #Mogollar #Adamlar None 0 1", Encoding.ASCII.GetString(bytes, 17, 41));
        }

        [Theory]
        [InlineData(1003, 1)]
        [InlineData(2001, 0)]
        public void WhisperDetailsGiveIdLevelAndSex(uint mesh, int male)
        {
            var record = new DbCharacter(1, 1000001, "Altay") { Mesh = mesh, Level = 70 };
            Assert.Equal($"1000001 70 70 # # None 0 {male}", MsgName.WhisperDetails(new Character(record)));
        }
    }
}

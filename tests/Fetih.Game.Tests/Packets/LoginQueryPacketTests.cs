namespace Fetih.Game.Tests.Packets
{
    using System;
    using Fetih.Game.Packets;
    using Xunit;

    public class LoginQueryPacketTests
    {
        private const string Footer = "5451436C69656E74";

        [Fact]
        public void TaskStatusQueryListsItsTaskIds()
        {
            var msg = new MsgTaskStatus();
            msg.Decode(Convert.FromHexString("2000" + "6E04" + "0300" + "0200"
                + "90010000" + "0000000000000000" + "A1030000" + "0000000000000000" + Footer));
            Assert.Equal(MsgTaskStatus.QueryAction, msg.Action);
            Assert.Equal(new uint[] { 0x190, 0x3A1 }, msg.TaskIds);
        }

        [Fact]
        public void TaskStatusCountIsCappedByTheBytesReceived()
        {
            var msg = new MsgTaskStatus();
            msg.Decode(Convert.FromHexString("1400" + "6E04" + "0300" + "5000" + "90010000" + "0000000000000000"));
            Assert.Equal(new uint[] { 0x190 }, msg.TaskIds);
        }

        [Fact]
        public void DataAskCarriesThePacketTypeAt8()
        {
            var msg = new MsgData();
            msg.Decode(Convert.FromHexString("2400" + "0904" + "04000000" + "1A270000" + new string('0', 48) + Footer));
            Assert.Equal((MsgData.AskMessageCountAction, 10010u), (msg.Action, msg.Values[0]));
        }

        [Fact]
        public void RankQueryHasItsModeAt4()
        {
            var msg = new MsgRank();
            msg.Decode(Convert.FromHexString("5000" + "7F04" + "02" + new string('0', 150) + Footer));
            Assert.Equal((MsgRank.OwnFlowerRankMode, 0u), (msg.Mode, msg.Data));
        }
    }
}

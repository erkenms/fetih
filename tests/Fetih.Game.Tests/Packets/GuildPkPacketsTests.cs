namespace Fetih.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Fetih.Game.Packets;
    using Xunit;

    public class GuildPkPacketsTests
    {
        [Fact]
        public void ContestRankingHasCountThenRankPointsAndNameEntries()
        {
            var msg = new MsgSynCompete { Action = MsgSynCompete.CompeteAction.Ranking };
            msg.Entries.Add((1, 300, "Lions"));
            msg.Entries.Add((2, 120, "Wolves"));
            var bytes = msg.Encode();
            Assert.Equal(16 + 2 * 48, bytes.Length);
            Assert.Equal(1062, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(0, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 64));
            Assert.Equal(120L, BitConverter.ToInt64(bytes, 68));
            Assert.Equal("Wolves", Encoding.ASCII.GetString(bytes, 76, 6));
        }

        [Fact]
        public void GuildPointsSitInTheFirstEntryAndExploitsAt12()
        {
            var points = MsgSynCompete.GuildPoints(77).Encode();
            Assert.Equal(1, BitConverter.ToUInt16(points, 4));
            Assert.Equal(77L, BitConverter.ToInt64(points, 20));
            var exploits = MsgSynCompete.OfExploits(9).Encode();
            Assert.Equal((2, 9u), (BitConverter.ToUInt16(exploits, 4), BitConverter.ToUInt32(exploits, 12)));
        }

        [Fact]
        public void MatchWindowHasNamesThenPointsMembersAndTimeLeft()
        {
            var bytes = new MsgFactionMatch
            {
                Action = MsgFactionMatch.MatchAction.Status, NameA = "Lions", PointsA = 30, MembersA = 4,
                NameB = "Wolves", PointsB = 12, MembersB = 6, SecondsLeft = 3600, Exploits = 5,
            }.Encode();
            Assert.Equal(MsgFactionMatch.Size, bytes.Length);
            Assert.Equal(2330, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal("Lions", Encoding.ASCII.GetString(bytes, 6, 5));
            Assert.Equal("Wolves", Encoding.ASCII.GetString(bytes, 42, 6));
            Assert.Equal((30u, 4u, 12u, 6u), (BitConverter.ToUInt32(bytes, 80), BitConverter.ToUInt32(bytes, 84),
                BitConverter.ToUInt32(bytes, 88), BitConverter.ToUInt32(bytes, 92)));
            Assert.Equal((3600u, 5u), (BitConverter.ToUInt32(bytes, 96), BitConverter.ToUInt32(bytes, 104)));
        }
    }
}

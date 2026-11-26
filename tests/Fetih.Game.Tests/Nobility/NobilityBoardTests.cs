namespace Fetih.Game.Tests.Nobility
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Fetih.Game.Nobility;
    using Fetih.Game.Packets;
    using Xunit;

    public class NobilityBoardTests
    {
        private const ulong Million = 1_000_000;

        // Donor i (from 1) has donated (count + 1 - i) million, so donor 1 gives most
        private static List<Donor> Donors(int count) =>
            NobilityBoard.Order(Enumerable.Range(1, count)
                .Select(i => new Donor((uint)i, "Hero" + i, 1003, (ulong)(count + 1 - i) * Million)));

        [Theory]
        [InlineData(0, NobilityRank.King)]
        [InlineData(2, NobilityRank.King)]
        [InlineData(3, NobilityRank.Prince)]
        [InlineData(14, NobilityRank.Prince)]
        [InlineData(15, NobilityRank.Duke)]
        [InlineData(49, NobilityRank.Duke)]
        public void TheTopFiftyAreRankedByPlace(int position, NobilityRank rank) =>
            Assert.Equal(rank, NobilityBoard.RankAt(position, 3 * Million));

        [Theory]
        [InlineData(3_000_000ul, NobilityRank.Commoner)]
        [InlineData(30_000_000ul, NobilityRank.Knight)]
        [InlineData(100_000_000ul, NobilityRank.Baron)]
        [InlineData(199_999_999ul, NobilityRank.Baron)]
        [InlineData(200_000_000ul, NobilityRank.Earl)]
        public void OthersAreRankedByDonation(ulong donation, NobilityRank rank) =>
            Assert.Equal(rank, NobilityBoard.RankAt(-1, donation));

        [Fact]
        public void DonorsPastTheBoardHaveNoPosition()
        {
            var standings = NobilityBoard.Rank(Donors(52));
            Assert.Equal(new Standing(NobilityRank.King, 0), standings[1]);
            Assert.Equal(new Standing(NobilityRank.Prince, 3), standings[4]);
            Assert.Equal(new Standing(NobilityRank.Duke, 49), standings[50]);
            Assert.Equal(new Standing(NobilityRank.Commoner, -1), standings[51]);
        }

        [Fact]
        public void EqualDonationsKeepTheirOrderByCharacter()
        {
            var ordered = NobilityBoard.Order(new[]
            {
                new Donor(7, "B", 0, 5 * Million), new Donor(3, "A", 0, 5 * Million),
                new Donor(9, "C", 0, 9 * Million), new Donor(4, "None", 0, 0),
            });
            Assert.Equal(new uint[] { 9, 3, 7 }, ordered.Select(d => d.ID));
        }

        [Fact]
        public void BoardRanksNeedOneMoreThanTheLastHolderExceptTheDonor()
        {
            var ordered = Donors(20);
            // Third place has 18 million; donor 10 doesn't count against themselves
            Assert.Equal(18 * Million + 1, NobilityBoard.TotalFor(NobilityRank.King, ordered, 10));
            Assert.Equal(17 * Million + 1, NobilityBoard.TotalFor(NobilityRank.King, ordered, 2));
            Assert.Equal(6 * Million + 1, NobilityBoard.TotalFor(NobilityRank.Prince, ordered, 30));
            // Fewer donors than duke places: one donation is enough
            Assert.Equal(NobilityBoard.MinDonation, NobilityBoard.TotalFor(NobilityRank.Duke, ordered, 30));
            Assert.Equal(200 * Million, NobilityBoard.TotalFor(NobilityRank.Earl, ordered, 30));
        }

        [Theory]
        [InlineData(30_000_000ul, 0ul, 30_000_000ul)]
        [InlineData(30_000_000ul, 29_000_000ul, 3_000_000ul)]
        [InlineData(30_000_000ul, 20_000_000ul, 10_000_000ul)]
        [InlineData(30_000_000ul, 30_000_000ul, 0ul)]
        public void RemainingIsAtLeastOneDonation(ulong total, ulong donation, ulong remaining) =>
            Assert.Equal(remaining, NobilityBoard.Remaining(total, donation));

        [Fact]
        public void PositionWithCountsOthersAtOrAbove()
        {
            var ordered = Donors(60);
            Assert.Equal(0, NobilityBoard.PositionWith(61 * Million, ordered, 100));
            Assert.Equal(2, NobilityBoard.PositionWith(59 * Million, ordered, 100));
            Assert.Equal(1, NobilityBoard.PositionWith(59 * Million, ordered, 2));
            Assert.Equal(-1, NobilityBoard.PositionWith(1 * Million, ordered, 100));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(10, 1)]
        [InlineData(11, 2)]
        [InlineData(80, 5)]
        public void TheBoardHasUpToFivePages(int donors, int pages) =>
            Assert.Equal(pages, NobilityBoard.Pages(donors));

        [Theory]
        [InlineData(3_000_000ul, 60ul)]
        [InlineData(3_000_001ul, 61ul)]
        public void ConquerPointsAreRoundedUp(ulong silver, ulong points) =>
            Assert.Equal(points, NobilityBoard.ConquerPointsFor(silver));

        [Fact]
        public void InfoCarriesTheRankStringAt32()
        {
            var bytes = MsgPeerage.Info(1000001, 35 * Million, new Standing(NobilityRank.Knight, -1)).Encode();
            Assert.Equal(2064, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(3, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 8));
            var text = "1000001 35000000 1 -1";
            Assert.Equal((1, text.Length), (bytes[32], bytes[33]));
            Assert.Equal(text, System.Text.Encoding.ASCII.GetString(bytes, 34, text.Length));
        }

        [Fact]
        public void BoardEntriesAre48BytesFrom32()
        {
            var donor = new Donor(1000001, "Hero", 2001003, 40 * Million);
            var msg = new MsgPeerage
            {
                Action = MsgPeerage.PeerageAction.Board, Data = 1, Pages = 2,
                Entries = new List<(Donor, bool, Standing)> { (donor, true, new Standing(NobilityRank.Duke, 12)) },
            };
            var bytes = msg.Encode();
            Assert.Equal(32 + MsgPeerage.EntrySize, bytes.Length);
            Assert.Equal((1, 2, 1), (BitConverter.ToUInt16(bytes, 8), BitConverter.ToUInt16(bytes, 10), BitConverter.ToInt32(bytes, 12)));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 32));
            Assert.Equal(1, bytes[36]);
            Assert.Equal(2001003u, BitConverter.ToUInt32(bytes, 40));
            Assert.Equal("Hero", System.Text.Encoding.ASCII.GetString(bytes, 44, 4));
            Assert.Equal(40 * Million, BitConverter.ToUInt64(bytes, 64));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 72));
            Assert.Equal(12, BitConverter.ToInt32(bytes, 76));
        }
    }
}

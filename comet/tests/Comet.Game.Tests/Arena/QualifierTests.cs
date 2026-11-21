namespace Comet.Game.Tests.Arena
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Arena;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Xunit;
    using static Comet.Game.Packets.MsgQualifyingInteractive;

    public class QualifierTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static ArenaRecord Record(uint id, uint points, uint winsToday = 0, uint lossesToday = 0) =>
            new ArenaRecord(new DbArena
            {
                CharacterID = id, Points = points, Day = ArenaRecord.DayOf(Now),
                WinsToday = winsToday, LossesToday = lossesToday,
            }) { Name = "Hero" + id, Level = 50 };

        [Theory]
        [InlineData(39, 0u)]
        [InlineData(40, 1500u)]
        [InlineData(90, 2200u)]
        [InlineData(100, 2700u)]
        [InlineData(110, 3200u)]
        [InlineData(130, 4000u)]
        public void PlayersStartWithPointsByLevel(byte level, uint points) =>
            Assert.Equal(points, ArenaRecord.StartingPoints(level));

        [Fact]
        public void ReachingLevel40GivesStartingPointsOnlyToThoseWhoNeverPlayed()
        {
            var fresh = new ArenaRecord(new DbArena { CharacterID = 1 }) { Level = 45 };
            fresh.GiveStartingPoints();
            Assert.Equal(1500u, fresh.Stored.Points);

            var broke = new ArenaRecord(new DbArena { CharacterID = 2, LossesTotal = 30 }) { Level = 45 };
            broke.GiveStartingPoints();
            Assert.Equal(0u, broke.Stored.Points);
        }

        [Fact]
        public void TheLoserGivesThreePercentAndTodayStartsOverEachDay()
        {
            var winner = Record(1, 1500, winsToday: 4);
            var loser = Record(2, 2000, lossesToday: 2);
            var (points, _, _) = ArenaRecord.Settle(winner, loser, Now);
            Assert.Equal(60u, points);
            Assert.Equal((1560u, 5u, 1u), (winner.Stored.Points, winner.Stored.WinsToday, winner.Stored.WinsTotal));
            Assert.Equal((1940u, 3u, 1u), (loser.Stored.Points, loser.Stored.LossesToday, loser.Stored.LossesTotal));

            ArenaRecord.Settle(winner, loser, Now.AddDays(1));
            Assert.Equal((1u, 0u), (winner.Stored.WinsToday, winner.Stored.LossesToday));
            Assert.Equal((0u, 1u), (loser.Stored.WinsToday, loser.Stored.LossesToday));
        }

        [Fact]
        public void TheTwentiethMatchAndNinthWinOfADayEarnHonor()
        {
            var winner = Record(1, 1500, winsToday: 8, lossesToday: 11);
            var loser = Record(2, 1500, lossesToday: 19);
            var (_, winnerBonuses, loserBonuses) = ArenaRecord.Settle(winner, loser, Now);
            Assert.Equal((2, 1), (winnerBonuses, loserBonuses));
            Assert.Equal((1000u, 1000u), (winner.Stored.HonorPoints, winner.Stored.HonorTotal));
            Assert.Equal(500u, loser.Stored.HonorPoints);

            (_, winnerBonuses, loserBonuses) = ArenaRecord.Settle(winner, loser, Now);
            Assert.Equal((0, 0), (winnerBonuses, loserBonuses));
        }

        [Fact]
        public void TodaysRankingHasOnlyPlayersWhoFoughtTodayByPointsThenWins()
        {
            var records = new List<ArenaRecord>
            {
                Record(1, 1600, winsToday: 1),
                Record(2, 1800, lossesToday: 1),
                Record(3, 1600, winsToday: 2),
                Record(4, 5000),
                new ArenaRecord(new DbArena { CharacterID = 5, Points = 9000, WinsToday = 3, Day = ArenaRecord.DayOf(Now) - 1 }),
            };
            Assert.Equal(new uint[] { 2, 3, 1 }, ArenaRecord.RankToday(records, Now).Select(r => r.ID));
        }

        [Fact]
        public void PlayersAreMatchedWithTheClosestPointsInTheOrderTheyWaited()
        {
            var pairs = Qualifier.Pairs(new[] { (1u, 1500u), (2u, 4000u), (3u, 1600u), (4u, 3900u), (5u, 2000u) });
            Assert.Equal(new[] { (1u, 3u), (2u, 4u) }, pairs);
        }

        [Fact]
        public void BothMustAcceptAndNoHitsCountBeforeTheFight()
        {
            var match = new ArenaMatch(1, 2, Now);
            Assert.False(match.Accept(1, Now, TimeSpan.FromSeconds(10)));
            Assert.False(match.Accept(3, Now, TimeSpan.FromSeconds(10)));
            Assert.Equal(1u, match.UnansweredWinner());
            Assert.True(match.Accept(2, Now.AddSeconds(1), TimeSpan.FromSeconds(10)));
            Assert.Equal(MatchState.Countdown, match.State);

            match.Hit(1, 100);
            Assert.False(match.Fight(Now.AddSeconds(10)));
            Assert.True(match.Fight(Now.AddSeconds(11)));
            Assert.False(match.Fight(Now.AddSeconds(12)));
            match.Hit(1, 100);
            match.Hit(2, 300);
            Assert.Equal((100u, 300u), (match.ScoreA, match.ScoreB));
            Assert.Equal(2u, match.WinnerByScore(coin: true));
        }

        [Fact]
        public void NobodyWinsWhileNeitherHasAnswered() =>
            Assert.Equal(0u, new ArenaMatch(1, 2, Now).UnansweredWinner());

        [Theory]
        [InlineData(true, 1u)]
        [InlineData(false, 2u)]
        public void ACoinBreaksAScoreTie(bool coin, uint winner) =>
            Assert.Equal(winner, new ArenaMatch(1, 2, Now).WinnerByScore(coin));

        [Fact]
        public void AMatchEndsOnce()
        {
            var match = new ArenaMatch(1, 2, Now);
            Assert.False(match.End(3, "killed", Now));
            Assert.True(match.End(2, "gave_up", Now));
            Assert.False(match.End(1, "killed", Now));
            Assert.Equal((MatchState.Ended, 2u, "gave_up"), (match.State, match.Winner, match.Reason));
        }

        [Fact]
        public void InteractiveCarriesAPlayer()
        {
            var fighter = new Fighter(1000002, "Altay", 1003, 52, 15, 4, 1545, 1, 0, 3, 2, 500, 1000);
            var bytes = new MsgQualifyingInteractive(QualifierAction.StartFight, 0, fighter).Encode();
            Assert.Equal(MsgQualifyingInteractive.Size, bytes.Length);
            Assert.Equal(2205, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(8u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal("Altay", System.Text.Encoding.ASCII.GetString(bytes, 16, 5));
            Assert.Equal((4u, 15u, 1545u, 52u), (BitConverter.ToUInt32(bytes, 32), BitConverter.ToUInt32(bytes, 36),
                BitConverter.ToUInt32(bytes, 44), BitConverter.ToUInt32(bytes, 48)));

            var msg = new MsgQualifyingInteractive();
            msg.Decode(Convert.FromHexString("34009D08" + "03000000" + "01000000" + new string('0', 88)));
            Assert.Equal((QualifierAction.Answer, Accept), (msg.Action, msg.Option));
        }

        [Fact]
        public void DetailInfoCarriesTheFightersRecord()
        {
            var fighter = new Fighter(1000002, "Altay", 1003, 52, 15, 4, 1545, 1, 2, 3, 4, 500, 1000);
            var details = new MsgQualifyingDetailInfo(fighter, QualifierStatus.Waiting).Encode();
            Assert.Equal(44, details.Length);
            Assert.Equal((4u, 1u, 3u, 4u, 1u, 2u, 1000u, 500u, 1545u), (BitConverter.ToUInt32(details, 4),
                BitConverter.ToUInt32(details, 12), BitConverter.ToUInt32(details, 16), BitConverter.ToUInt32(details, 20),
                BitConverter.ToUInt32(details, 24), BitConverter.ToUInt32(details, 28), BitConverter.ToUInt32(details, 32),
                BitConverter.ToUInt32(details, 36), BitConverter.ToUInt32(details, 40)));
        }

        [Fact]
        public void FightersListIs148BytesWithTwoFightersPerMatch()
        {
            var fighter = new Fighter(1000002, "Altay", 1003, 52, 15, 4, 1545, 1, 2, 3, 4, 500, 1000);
            Assert.Equal(148, new MsgQualifyingFightersList { Page = 1 }.Encode().Length);
            var list = new MsgQualifyingFightersList { Page = 1, Matches = 1, Waiting = 3 };
            list.Shown.Add((fighter, fighter with { ID = 1000003 }));
            var bytes = list.Encode();
            Assert.Equal(148, bytes.Length);
            Assert.Equal((1u, 3u, 1u), (BitConverter.ToUInt32(bytes, 12), BitConverter.ToUInt32(bytes, 16), BitConverter.ToUInt32(bytes, 24)));
            Assert.Equal((1000002u, 1000003u), (BitConverter.ToUInt32(bytes, 28), BitConverter.ToUInt32(bytes, 88)));
        }

        [Fact]
        public void RankIs52Bytes() => Assert.Equal(52, new MsgQualifyingRank().Encode().Length);

        [Fact]
        public void SeasonRankListIs60Bytes() => Assert.Equal(60, new MsgQualifyingSeasonRankList().Encode().Length);

        [Fact]
        public void ArenicScoreIs56Bytes() => Assert.Equal(56, new MsgArenicScore().Encode().Length);

        [Fact]
        public void RankQueriesCarryKindAndPage()
        {
            var rank = new MsgQualifyingRank();
            rank.Decode(Convert.FromHexString("34009F08" + "0100" + "0200" + new string('0', 88)));
            Assert.Equal((MsgQualifyingRank.RankKind.Honor, (ushort)2), (rank.Kind, rank.Page));
        }
    }
}

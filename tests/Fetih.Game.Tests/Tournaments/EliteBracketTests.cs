namespace Fetih.Game.Tests.Tournaments
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Fetih.Game.Packets;
    using Fetih.Game.Tournaments;
    using Xunit;

    public class EliteBracketTests
    {
        // Plays every match of the bracket; the lower ID always wins
        private static List<EliteRound> PlayOut(EliteBracket bracket)
        {
            var random = new Random(7);
            var rounds = new List<EliteRound>();
            while (!bracket.Finished)
            {
                var round = bracket.NextRound(random);
                Assert.NotNull(round);
                rounds.Add(round);
                var pairs = round.Pairs.ToList();
                if (round.ThirdPlace is (uint a, uint b))
                    pairs.Add((a, b));
                foreach (var (x, y) in pairs)
                    bracket.Record(Math.Min(x, y), Math.Max(x, y));
            }
            return rounds;
        }

        [Fact]
        public void TwentyPlayersKnockOutToEightThenPlayTheFinalPhase()
        {
            var bracket = new EliteBracket(Enumerable.Range(1, 20).Select(i => (uint)i));
            var rounds = PlayOut(bracket);

            // 20 -> 10 -> 5: two knock-out rounds, then quarterfinal (5 -> 3), semifinal (3 -> 2), final
            Assert.Equal(new[] { EliteStage.Knockout, EliteStage.Knockout, EliteStage.Quarterfinal,
                EliteStage.Semifinal, EliteStage.Final }, rounds.Select(r => r.Stage));
            var places = bracket.Placements();
            Assert.Equal(5, places.Count);
            Assert.Equal((1u, EliteBracket.Champion), places[0]);
            Assert.Single(places, p => p.Rank == EliteBracket.Second);
            Assert.Single(places, p => p.Rank == EliteBracket.Third);
            Assert.Equal(2, places.Count(p => p.Rank == EliteBracket.TopEight));
        }

        [Fact]
        public void EightPlayersAreAllTheFinalEightAndTheSemifinalLosersFightForThird()
        {
            var bracket = new EliteBracket(Enumerable.Range(1, 8).Select(i => (uint)i));
            var rounds = PlayOut(bracket);

            Assert.Equal(new[] { EliteStage.Quarterfinal, EliteStage.Semifinal, EliteStage.Final },
                rounds.Select(r => r.Stage));
            Assert.NotNull(rounds[2].ThirdPlace);
            var places = bracket.Placements();
            Assert.Equal(8, places.Count);
            Assert.Equal(new[] { 1, 2, 3, 4, 4, 4, 4, 4 }, places.Select(p => p.Rank));
            Assert.Equal(1u, places[0].ID);
        }

        [Fact]
        public void TwoPlayersGoStraightToTheFinal()
        {
            var bracket = new EliteBracket(new uint[] { 10, 20 });
            var round = bracket.NextRound(new Random(1));

            Assert.Equal(EliteStage.Final, round.Stage);
            Assert.Null(round.ThirdPlace);
            Assert.Null(bracket.NextRound(new Random(1)));
            bracket.Record(20, 10);
            Assert.True(bracket.Finished);
            Assert.Equal(new[] { (20u, 1), (10u, 2) }, bracket.Placements());
        }

        [Fact]
        public void ALoneSemifinalLoserTakesThirdWithoutAMatch()
        {
            var bracket = new EliteBracket(new uint[] { 1, 2, 3 });
            var semi = bracket.NextRound(new Random(3));
            Assert.Equal(EliteStage.Semifinal, semi.Stage);
            Assert.NotEqual(0u, semi.Bye);
            var (a, b) = semi.Pairs.Single();
            bracket.Record(a, b);
            Assert.True(bracket.AwaitsThirdPlace(b));

            var final = bracket.NextRound(new Random(3));
            Assert.Null(final.ThirdPlace);
            Assert.False(bracket.AwaitsThirdPlace(b));
            var (x, y) = final.Pairs.Single();
            bracket.Record(x, y);
            Assert.Equal(new[] { (x, 1), (y, 2), (b, 3) }, bracket.Placements());
        }

        [Fact]
        public void APlayerWhoQuitsBetweenRoundsIsOutWithoutAPlace()
        {
            var bracket = new EliteBracket(new uint[] { 1, 2, 3, 4 });
            var semi = bracket.NextRound(new Random(5));
            foreach (var (a, b) in semi.Pairs)
                bracket.Record(Math.Min(a, b), Math.Max(a, b));
            uint quitter = semi.Pairs.Max(p => Math.Max(p.A, p.B));
            bracket.Withdraw(quitter);

            var final = bracket.NextRound(new Random(5));
            Assert.Null(final.ThirdPlace);
            var (x, y) = final.Pairs.Single();
            bracket.Record(Math.Min(x, y), Math.Max(x, y));
            Assert.DoesNotContain(bracket.Placements(), p => p.ID == quitter);
            Assert.Contains(bracket.Placements(), p => p.Rank == EliteBracket.Third);
        }

        [Fact]
        public void TheLastPlayerStandingIsChampion()
        {
            var bracket = new EliteBracket(new uint[] { 1, 2 });
            bracket.Withdraw(2);
            Assert.True(bracket.Finished);
            Assert.Null(bracket.NextRound(new Random(1)));
            Assert.Equal(new[] { (1u, 1) }, bracket.Placements());
        }

        [Fact]
        public void PrizesFollowTheItemTypeOrder()
        {
            // Refined, Unique, Elite, Super Champion packs, then 2nd, 3rd and Top 8
            Assert.Equal(720714u, ElitePk.PackOf(0, 1));
            Assert.Equal(720717u, ElitePk.PackOf(3, 1));
            Assert.Equal(720720u, ElitePk.PackOf(2, 2));
            Assert.Equal(720723u, ElitePk.PackOf(1, 3));
            Assert.Equal(720729u, ElitePk.PackOf(3, 4));
            Assert.Equal((0, 1, 1, 2, 3), (ElitePk.GroupOf(99), ElitePk.GroupOf(100), ElitePk.GroupOf(119),
                ElitePk.GroupOf(120), ElitePk.GroupOf(130)));
            Assert.Equal((100, 50, 30, 15, 0), (ElitePk.ExpPercentOf(0, 1), ElitePk.ExpPercentOf(1, 2),
                ElitePk.ExpPercentOf(2, 3), ElitePk.ExpPercentOf(2, 4), ElitePk.ExpPercentOf(3, 1)));
        }

        [Fact]
        public void TimeTiesGoToBattlePowerThenLevel()
        {
            Assert.True(ElitePk.Compare(20, 100, 10, 130) > 0);
            Assert.True(ElitePk.Compare(10, 100, 10, 130) < 0);
            Assert.True(ElitePk.Compare(10, 130, 10, 130) == 0);
        }

        [Fact]
        public void PacketsPutTheirFieldsWhereTheClientReadsThem()
        {
            var start = new MsgElitePKArenic(MsgElitePKArenic.ArenicAction.Start) { Seconds = 180 }.Encode();
            Assert.Equal(MsgElitePKArenic.Size, start.Length);
            Assert.Equal(2218, BitConverter.ToUInt16(start, 2));
            Assert.Equal(2, BitConverter.ToUInt16(start, 4));
            Assert.Equal(180, BitConverter.ToUInt16(start, 44));
            var won = new MsgElitePKArenic(MsgElitePKArenic.ArenicAction.Result) { Won = true }.Encode();
            Assert.Equal((3, 1u), (BitConverter.ToUInt16(won, 4), BitConverter.ToUInt32(won, 8)));

            var score = new MsgElitePKScore { IdA = 1, NameA = "Ann", DamageA = 500, IdB = 2, NameB = "Bob", DamageB = 70 }.Encode();
            Assert.Equal(MsgElitePKScore.Size, score.Length);
            Assert.Equal(2222, BitConverter.ToUInt16(score, 2));
            Assert.Equal((1u, 500u, 2u, 70u), (BitConverter.ToUInt32(score, 4), BitConverter.ToUInt32(score, 24),
                BitConverter.ToUInt32(score, 28), BitConverter.ToUInt32(score, 48)));
            Assert.Equal((byte)'B', score[32]);
        }
    }
}

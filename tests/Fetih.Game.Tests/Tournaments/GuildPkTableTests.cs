namespace Fetih.Game.Tests.Tournaments
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Fetih.Game.Events;
    using Fetih.Game.Tournaments;
    using Xunit;

    public class GuildPkTableTests
    {
        // Guild IDs 101-108 placed 1-8 in the contest
        private static List<GuildPkEntry> Qualifiers(int count = 8) =>
            Enumerable.Range(1, count).Select(seed => new GuildPkEntry
            {
                GuildID = (uint)(100 + seed), Name = "G" + seed, Seed = seed,
            }).ToList();

        private static GuildPkEntry Seed(IEnumerable<GuildPkEntry> entries, int seed) => entries.Single(e => e.Seed == seed);

        [Fact]
        public void TheContestRanksByPointsThenOlderGuildAndKeepsTheTopEight()
        {
            var points = new Dictionary<uint, long> { [5] = 10, [3] = 30, [9] = 10, [1] = 0 };
            Assert.Equal(new[] { (3u, 30L), (5u, 10L) }, GuildPkTable.Top(points, 2));
            Assert.Equal(3, GuildPkTable.Top(points, 8).Count);
        }

        [Fact]
        public void OddSeedsPlayInGroupAAndEvenSeedsInGroupB()
        {
            var entries = Qualifiers();
            Assert.Equal(new[] { 1, 3, 5, 7 }, entries.Where(e => e.Half == 0).Select(e => e.Seed));
            Assert.Equal(new[] { 2, 4, 6, 8 }, entries.Where(e => e.Half == 1).Select(e => e.Seed));
        }

        [Fact]
        public void ThreeRoundsMeetEveryOtherGuildOfTheGroupOnce()
        {
            var entries = Qualifiers();
            var met = new HashSet<(uint, uint)>();
            for (int round = 1; round <= GuildPkTable.Rounds; round++)
            {
                var pairs = GuildPkTable.Round(entries, round);
                Assert.Equal(4, pairs.Count);
                // Every guild plays once a round, within its group
                Assert.Equal(8, pairs.SelectMany(p => new[] { p.A.GuildID, p.B.GuildID }).Distinct().Count());
                Assert.All(pairs, p => Assert.Equal(p.A.Half, p.B.Half));
                foreach (var p in pairs)
                    Assert.True(met.Add((Math.Min(p.A.GuildID, p.B.GuildID), Math.Max(p.A.GuildID, p.B.GuildID))));
            }
            Assert.Equal(12, met.Count);
        }

        [Fact]
        public void AWinIsThreePointsADrawOneAndALossNone()
        {
            var entries = Qualifiers();
            var pairs = GuildPkTable.Round(entries, 1);
            // Group A round 1: 1 vs 7 and 3 vs 5
            var first = pairs.Single(p => p.A.Seed == 1);
            Assert.Equal(7, first.B.Seed);
            GuildPkTable.RecordRound(first, 120, 40);
            var second = pairs.Single(p => p.A.Seed == 3);
            GuildPkTable.RecordRound(second, 60, 60);

            Assert.Equal((3, 120L), (Seed(entries, 1).Points, Seed(entries, 1).Score));
            Assert.Equal((0, 40L), (Seed(entries, 7).Points, Seed(entries, 7).Score));
            Assert.Equal(1, Seed(entries, 3).Points);
            Assert.Equal(1, Seed(entries, 5).Points);
            Assert.All(new[] { 1, 3, 5, 7 }, s => Assert.Equal(1, Seed(entries, s).Played));
        }

        [Fact]
        public void StandingsGoByPointsThenScoreThenSeed()
        {
            var entries = Qualifiers();
            Seed(entries, 1).Points = 4; Seed(entries, 1).Score = 10;
            Seed(entries, 3).Points = 4; Seed(entries, 3).Score = 50;
            Seed(entries, 5).Points = 6;
            Seed(entries, 7).Points = 4; Seed(entries, 7).Score = 50;
            Assert.Equal(new[] { 5, 3, 7, 1 }, GuildPkTable.Standings(entries, 0).Select(e => e.Seed));
        }

        [Fact]
        public void TheFinalPairsEqualPlacesAndTheWinnersTakeOddRanks()
        {
            var entries = Qualifiers();
            // Group A finishes 7, 5, 3, 1; Group B 2, 4, 6, 8
            foreach (var (seed, points) in new[] { (7, 9), (5, 6), (3, 3), (1, 0), (2, 9), (4, 6), (6, 3), (8, 0) })
                Seed(entries, seed).Points = points;
            var final = GuildPkTable.Final(entries);
            Assert.Equal(new[] { (7, 2, 1), (5, 4, 3), (3, 6, 5), (1, 8, 7) },
                final.Select(p => (p.A.Seed, p.B.Seed, p.Place)));

            Assert.Equal(2, GuildPkTable.RecordFinal(final[0], 10, 300).Seed);
            // A tie goes to the better ranked: 5 and 4 have equal points and score, so the lower seed
            Assert.Equal(4, GuildPkTable.RecordFinal(final[1], 0, 0).Seed);
            GuildPkTable.RecordFinal(final[2], 5, 1);
            GuildPkTable.RecordFinal(final[3], 0, 1);
            Assert.Equal(new[] { (2, 1), (7, 2), (4, 3), (5, 4), (3, 5), (6, 6), (8, 7), (1, 8) },
                entries.OrderBy(e => e.FinalRank).Select(e => (e.Seed, e.FinalRank)));
        }

        [Fact]
        public void AGroupShortOfGuildsGivesWalkovers()
        {
            // Five qualifiers: Group A has 1, 3, 5; Group B has 2, 4
            var entries = Qualifiers(5);
            var round = GuildPkTable.Round(entries, 1);
            // Round 1 pairs places 1-4 and 2-3: the missing 4th of A and 3rd and 4th of B
            Assert.Equal(new[] { 1, 2, 4 }, round.Where(p => p.Walkover).Select(p => p.A.Seed).OrderBy(s => s));
            var walkover = round.First(p => p.Walkover);
            GuildPkTable.RecordRound(walkover, 0, 0);
            Assert.Equal((1, 3), (Seed(entries, 1).Played, Seed(entries, 1).Points));

            var final = GuildPkTable.Final(entries);
            Assert.Equal(3, final.Count);
            Assert.True(final[2].Walkover);
            Assert.Same(GuildPkTable.RecordFinal(final[2], 0, 0), final[2].A);
            Assert.Equal(5, final[2].A.FinalRank);
        }

        [Fact]
        public void TheContestAndTournamentFollowTheHelpPageTimes()
        {
            var schedule = EventSchedule.Empty.With(GuildPk.Events);
            // 2026-11-23 is a Monday
            var monday = new DateTime(2026, 11, 23);
            var week = schedule.All.SelectMany(ev => EventSchedule.Edges(ev, monday, monday.AddDays(7))
                .Select(e => (ev.Name, e.Edge, e.At))).ToList();

            Assert.Contains((GuildPk.ContestName, EventEdge.SignUpOpens, monday.AddHours(20).AddMinutes(25)), week);
            Assert.Contains((GuildPk.ContestName, EventEdge.Starts, monday.AddHours(20).AddMinutes(30)), week);
            Assert.Contains((GuildPk.ContestName, EventEdge.Ends, monday.AddHours(22).AddMinutes(30)), week);
            Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday },
                week.Where(w => w.Name == GuildPk.RoundName && w.Edge == EventEdge.Starts).Select(w => w.At.DayOfWeek));
            Assert.Contains((GuildPk.FinalName, EventEdge.Starts, monday.AddDays(5).AddHours(21).AddMinutes(30)), week);
            Assert.Contains((GuildPk.FinalName, EventEdge.Ends, monday.AddDays(5).AddHours(23).AddMinutes(30)), week);
        }

        [Fact]
        public void AddedEventsDontReplaceTheClientsOwn()
        {
            var own = new ScheduledEvent(1001, "Mine", new HashSet<DayOfWeek> { DayOfWeek.Monday }, 1, 255,
                new[] { new EventWindow(TimeSpan.FromHours(1), TimeSpan.FromHours(2), null) });
            var schedule = new EventSchedule(new[] { own }).With(GuildPk.Events);
            Assert.Equal(new[] { "Mine", GuildPk.RoundName, GuildPk.FinalName }, schedule.All.Select(e => e.Name));
        }

        [Fact]
        public void MembersFightAfterAWeekInTheGuild()
        {
            var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
            var zone = Clock.Zone;
            Clock.Zone = TimeZoneInfo.Utc;
            try
            {
                Assert.True(GuildPk.LongEnough(20261118, now));
                Assert.False(GuildPk.LongEnough(20261119, now));
            }
            finally { Clock.Zone = zone; }
        }

        [Fact]
        public void TournamentPrizesLastUntilNextSaturdayEvening()
        {
            Assert.Equal(new DateTime(2026, 12, 5, 20, 30, 0), GuildPk.NextSaturdayEvening(new DateTime(2026, 11, 28, 23, 30, 0)));
            Assert.Equal(new DateTime(2026, 11, 28, 20, 30, 0), GuildPk.NextSaturdayEvening(new DateTime(2026, 11, 25, 9, 0, 0)));
        }
    }
}

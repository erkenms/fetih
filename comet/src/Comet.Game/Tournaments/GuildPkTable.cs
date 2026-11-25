namespace Comet.Game.Tournaments
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // A qualified guild's week in the Guild PK Tournament. Seed is its place in the Guild
    // Contest (1-8), Points its round-robin points, Score the guild points it earned in its
    // matches (the "history score" that breaks ties), FinalRank its place after the final.
    public sealed class GuildPkEntry
    {
        public uint GuildID { get; init; }
        public string Name { get; set; }
        public int Seed { get; init; }
        public long ContestPoints { get; init; }
        public int Played { get; set; }
        public int Points { get; set; }
        public long Score { get; set; }
        public int FinalRank { get; set; }

        public int Half => GuildPkTable.HalfOf(this.Seed);
    }

    public sealed record GuildPkPairing(GuildPkEntry A, GuildPkEntry B, int Place)
    {
        // A half short of guilds leaves one without an opponent: it wins
        public bool Walkover => this.B == null;
    }

    // The contest's top 8 split into two halves: seeds 1, 3, 5, 7 (Group A) and 2, 4, 6, 8
    // (Group B). Each half plays three round-robin rounds (3 points a win, 1 a draw), ranked by
    // points, then score, then seed. In the final the same places of the two halves meet:
    // 1st vs 1st for champion and 2nd place, 2nd vs 2nd for 3rd and 4th, and so on.
    public static class GuildPkTable
    {
        public const int Qualifiers = 8, Rounds = 3, PerHalf = 4;
        public const int WinPoints = 3, DrawPoints = 1;

        // Places in the half (0-3) meeting in each round; every pair once
        private static readonly (int, int)[][] Pairings =
        {
            new[] { (0, 3), (1, 2) },
            new[] { (0, 2), (3, 1) },
            new[] { (0, 1), (2, 3) },
        };

        public static int HalfOf(int seed) => (seed - 1) % 2;

        public static string HalfName(int half) => half == 0 ? "Group A" : "Group B";

        // The contest's ranking: most points first, ties to the older guild (lower ID)
        public static List<(uint GuildID, long Points)> Top(IReadOnlyDictionary<uint, long> points, int count) =>
            points.Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value).ThenBy(p => p.Key)
                .Take(count).Select(p => (p.Key, p.Value)).ToList();

        // The pairs of a round (1-3), Group A first. A missing place leaves a walkover.
        public static List<GuildPkPairing> Round(IEnumerable<GuildPkEntry> entries, int round)
        {
            if (round < 1 || round > Rounds)
                throw new ArgumentOutOfRangeException(nameof(round));
            var pairs = new List<GuildPkPairing>();
            foreach (int half in new[] { 0, 1 })
            {
                var places = entries.Where(e => e.Half == half).OrderBy(e => e.Seed).ToList();
                foreach (var (x, y) in Pairings[round - 1])
                {
                    var a = x < places.Count ? places[x] : null;
                    var b = y < places.Count ? places[y] : null;
                    if (a == null && b == null)
                        continue;
                    pairs.Add(a == null ? new GuildPkPairing(b, null, 0) : new GuildPkPairing(a, b, 0));
                }
            }
            return pairs;
        }

        public static List<GuildPkEntry> Standings(IEnumerable<GuildPkEntry> entries, int half) =>
            entries.Where(e => e.Half == half).OrderBy(e => e, Ranking).ToList();

        // Better first: more points, more score, lower seed
        public static readonly IComparer<GuildPkEntry> Ranking = Comparer<GuildPkEntry>.Create((a, b) =>
            a.Points != b.Points ? b.Points.CompareTo(a.Points) :
            a.Score != b.Score ? b.Score.CompareTo(a.Score) :
            a.Seed.CompareTo(b.Seed));

        public static void RecordRound(GuildPkPairing pair, long scoreA, long scoreB)
        {
            pair.A.Played++;
            if (pair.Walkover)
            {
                pair.A.Points += WinPoints;
                return;
            }
            pair.B.Played++;
            pair.A.Score += scoreA;
            pair.B.Score += scoreB;
            if (scoreA == scoreB)
            {
                pair.A.Points += DrawPoints;
                pair.B.Points += DrawPoints;
            }
            else
                (scoreA > scoreB ? pair.A : pair.B).Points += WinPoints;
        }

        // The final's pairs; Place is the rank its winner takes (1, 3, 5, 7)
        public static List<GuildPkPairing> Final(IEnumerable<GuildPkEntry> entries)
        {
            var a = Standings(entries, 0);
            var b = Standings(entries, 1);
            var pairs = new List<GuildPkPairing>();
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                var x = i < a.Count ? a[i] : null;
                var y = i < b.Count ? b[i] : null;
                pairs.Add(x == null ? new GuildPkPairing(y, null, 2 * i + 1) : new GuildPkPairing(x, y, 2 * i + 1));
            }
            return pairs;
        }

        // The winner takes the pair's place, the loser the next; a tie goes to the better ranked
        public static GuildPkEntry RecordFinal(GuildPkPairing pair, long scoreA, long scoreB)
        {
            if (pair.Walkover)
            {
                pair.A.FinalRank = pair.Place;
                return pair.A;
            }
            pair.A.Score += scoreA;
            pair.B.Score += scoreB;
            var winner = scoreA != scoreB
                ? (scoreA > scoreB ? pair.A : pair.B)
                : (Ranking.Compare(pair.A, pair.B) <= 0 ? pair.A : pair.B);
            var loser = winner == pair.A ? pair.B : pair.A;
            winner.FinalRank = pair.Place;
            loser.FinalRank = pair.Place + 1;
            return winner;
        }
    }
}

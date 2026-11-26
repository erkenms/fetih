namespace Fetih.Game.Tournaments
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public enum EliteStage { Knockout, Quarterfinal, Semifinal, Final }

    public sealed record EliteRound(EliteStage Stage, IReadOnlyList<(uint A, uint B)> Pairs, uint Bye,
        (uint A, uint B)? ThirdPlace);

    // A knock-out bracket of entrant IDs: characters in the Elite PK, team leaders in the Team
    // PK tournaments. Rounds pair the entrants still in at random, an odd one out going
    // through; who loses is out. With eight or fewer left the final phase starts: those are the
    // final eight. The two semifinal losers fight for third place alongside the final. Not
    // thread-safe; the tournament changes it under its lock.
    public sealed class EliteBracket
    {
        public const int FinalSize = 8;
        public const int Champion = 1, Second = 2, Third = 3, TopEight = 4;

        private readonly List<uint> alive;
        private readonly HashSet<uint> finalEight = new HashSet<uint>();
        private readonly List<uint> semifinalLosers = new List<uint>();
        private readonly Dictionary<uint, int> ranks = new Dictionary<uint, int>();
        private readonly HashSet<uint> pending = new HashSet<uint>();
        private (uint A, uint B)? thirdPlacePending;
        private EliteStage stage;

        public EliteBracket(IEnumerable<uint> entrants) => this.alive = entrants.Distinct().ToList();

        public IReadOnlyList<uint> Alive => this.alive;
        public int RoundNumber { get; private set; }
        public EliteStage Stage => this.stage;
        public bool InFinal => this.finalEight.Count > 0;
        public bool RoundOpen => this.pending.Count > 0 || this.thirdPlacePending != null;
        public bool Finished => !this.RoundOpen && this.alive.Count <= 1 && this.thirdPlacePending == null;

        public static EliteStage StageFor(int alive) =>
            alive > FinalSize ? EliteStage.Knockout :
            alive > 4 ? EliteStage.Quarterfinal :
            alive > 2 ? EliteStage.Semifinal :
            EliteStage.Final;

        // Null when the bracket is decided or a round is still being fought
        public EliteRound NextRound(Random random)
        {
            if (this.RoundOpen || this.alive.Count <= 1)
                return null;
            this.stage = StageFor(this.alive.Count);
            if (this.stage != EliteStage.Knockout && this.finalEight.Count == 0)
                this.finalEight.UnionWith(this.alive);
            this.RoundNumber++;

            var order = this.alive.OrderBy(_ => random.Next()).ToList();
            var pairs = new List<(uint, uint)>();
            for (int i = 0; i + 1 < order.Count; i += 2)
                pairs.Add((order[i], order[i + 1]));
            uint bye = order.Count % 2 == 1 ? order[^1] : 0;
            foreach (var (a, b) in pairs)
            {
                this.pending.Add(a);
                this.pending.Add(b);
            }

            (uint, uint)? third = null;
            if (this.stage == EliteStage.Final)
            {
                if (this.semifinalLosers.Count >= 2)
                    third = this.thirdPlacePending = (this.semifinalLosers[0], this.semifinalLosers[1]);
                else if (this.semifinalLosers.Count == 1)
                    this.ranks[this.semifinalLosers[0]] = Third;
            }
            return new EliteRound(this.stage, pairs, bye, third);
        }

        // A semifinal loser yet to fight for third place
        public bool AwaitsThirdPlace(uint id) => this.semifinalLosers.Contains(id) && !this.ranks.ContainsKey(id);

        public bool IsThirdPlaceMatch(uint a, uint b) =>
            this.thirdPlacePending is (uint x, uint y) && ((x == a && y == b) || (x == b && y == a));

        // A match of the open round is decided
        public void Record(uint winner, uint loser)
        {
            if (this.IsThirdPlaceMatch(winner, loser))
            {
                this.thirdPlacePending = null;
                this.ranks[winner] = Third;
                return;
            }
            if (!this.pending.Remove(winner) | !this.pending.Remove(loser))
                return;
            this.alive.Remove(loser);
            if (this.stage == EliteStage.Semifinal)
                this.semifinalLosers.Add(loser);
            else if (this.stage == EliteStage.Final)
            {
                this.ranks[winner] = Champion;
                this.ranks[loser] = Second;
            }
        }

        // A player quit between matches: out, with no placement
        public void Withdraw(uint id)
        {
            this.alive.Remove(id);
            this.finalEight.Remove(id);
            this.semifinalLosers.Remove(id);
            this.ranks.Remove(id);
            if (this.thirdPlacePending is (uint a, uint b) && (a == id || b == id))
            {
                this.thirdPlacePending = null;
                this.ranks[a == id ? b : a] = Third;
            }
        }

        // Champion 1, second 2, third 3, the rest of the final eight 4. A lone survivor of a
        // bracket that ran out of opponents is champion.
        public IReadOnlyList<(uint ID, int Rank)> Placements()
        {
            var result = new Dictionary<uint, int>(this.ranks);
            if (this.alive.Count == 1 && !result.ContainsValue(Champion))
                result[this.alive[0]] = Champion;
            foreach (var id in this.finalEight)
                result.TryAdd(id, TopEight);
            return result.OrderBy(r => r.Value).ThenBy(r => r.Key).Select(r => (r.Key, r.Value)).ToList();
        }
    }
}

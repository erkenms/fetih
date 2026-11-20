namespace Comet.Game.Arena
{
    using System;
    using Comet.Game.States;
    using Comet.Game.World;

    public enum MatchState
    {
        // Both players were asked to accept
        Invited,
        // Both accepted and are moved into the arena; no attacks yet
        Countdown,
        Fighting,
        // Decided; fighters in the arena leave it after the qualifier's EndTime
        Ended,
    }

    // One qualifier match between two players, by character ID. Changed under the
    // qualifier's lock.
    public sealed class ArenaMatch
    {
        public uint A { get; }
        public uint B { get; }
        public MatchState State { get; private set; } = MatchState.Invited;
        public DateTime InvitedAt { get; }
        public DateTime FightAt { get; private set; }
        public DateTime EndedAt { get; private set; }
        public uint ScoreA { get; private set; }
        public uint ScoreB { get; private set; }
        public uint Winner { get; private set; }
        public string Reason { get; private set; }

        // The arena both fight in, a copy of the qualifier map of their own
        public GameMap<Client> Map { get; set; }

        // Where each fighter was before the arena
        public MapPoint ReturnA { get; private set; }
        public MapPoint ReturnB { get; private set; }

        private bool acceptedA, acceptedB, enteredA, enteredB;

        public ArenaMatch(uint a, uint b, DateTime now)
        {
            this.A = a;
            this.B = b;
            this.InvitedAt = now;
        }

        public bool Has(uint id) => id == this.A || id == this.B;
        public uint Opponent(uint id) => id == this.A ? this.B : this.A;
        public bool Accepted(uint id) => id == this.A ? this.acceptedA : this.acceptedB;
        public bool Entered(uint id) => id == this.A ? this.enteredA : this.enteredB;
        public MapPoint ReturnOf(uint id) => id == this.A ? this.ReturnA : this.ReturnB;
        public uint ScoreOf(uint id) => id == this.A ? this.ScoreA : this.ScoreB;

        // In the arena or about to be: from both accepting until the end
        public bool InArena => this.State == MatchState.Countdown || this.State == MatchState.Fighting;

        // True when this answer is the second acceptance; the match then counts down
        public bool Accept(uint id, DateTime now, TimeSpan countdown)
        {
            if (this.State != MatchState.Invited || !this.Has(id))
                return false;
            if (id == this.A)
                this.acceptedA = true;
            else
                this.acceptedB = true;
            if (!this.acceptedA || !this.acceptedB)
                return false;
            this.State = MatchState.Countdown;
            this.FightAt = now + countdown;
            return true;
        }

        public void SetReturn(uint id, MapPoint returnTo)
        {
            if (id == this.A)
                this.ReturnA = returnTo;
            else if (id == this.B)
                this.ReturnB = returnTo;
        }

        // The fighter stands in the arena
        public void Enter(uint id)
        {
            if (id == this.A)
                this.enteredA = true;
            else if (id == this.B)
                this.enteredB = true;
        }

        // True once, when the countdown is over
        public bool Fight(DateTime now)
        {
            if (this.State != MatchState.Countdown || now < this.FightAt)
                return false;
            this.State = MatchState.Fighting;
            return true;
        }

        public void Hit(uint attacker, uint damage)
        {
            if (this.State != MatchState.Fighting)
                return;
            if (attacker == this.A)
                this.ScoreA += damage;
            else if (attacker == this.B)
                this.ScoreB += damage;
        }

        // The one who accepted wins an invitation nobody answered in time; 0 if neither did
        public uint UnansweredWinner() =>
            this.acceptedA && !this.acceptedB ? this.A :
            this.acceptedB && !this.acceptedA ? this.B : 0;

        // More damage wins when time is up; a tie is decided by the coin
        public uint WinnerByScore(bool coin) =>
            this.ScoreA > this.ScoreB ? this.A :
            this.ScoreB > this.ScoreA ? this.B :
            coin ? this.A : this.B;

        // False if the match was already decided
        public bool End(uint winner, string reason, DateTime now)
        {
            if (this.State == MatchState.Ended || !this.Has(winner))
                return false;
            this.State = MatchState.Ended;
            this.Winner = winner;
            this.Reason = reason;
            this.EndedAt = now;
            return true;
        }
    }
}

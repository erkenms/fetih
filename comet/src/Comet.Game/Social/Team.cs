namespace Comet.Game.Social
{
    using System.Collections.Generic;
    using Comet.Game.States;

    // Only changed under Teams' lock
    public sealed class Team
    {
        public const int Capacity = 5;

        public Client Leader { get; }

        // The leader first
        public List<Client> Members { get; } = new List<Client>();

        public HashSet<uint> Invited { get; } = new HashSet<uint>();
        public HashSet<uint> Applicants { get; } = new HashSet<uint>();

        public Team(Client leader)
        {
            this.Leader = leader;
            this.Members.Add(leader);
        }

        public bool Full => this.Members.Count >= Capacity;
    }
}

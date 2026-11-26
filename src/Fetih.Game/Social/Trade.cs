namespace Fetih.Game.Social
{
    using System.Collections.Generic;
    using Fetih.Game.Database.Models;
    using Fetih.Game.States;

    // An open trade window between two players. Only changed under Trading's gate.
    public sealed class Trade
    {
        public sealed class Offer
        {
            public Client Player { get; init; }
            public List<DbItem> Items { get; } = new List<DbItem>();
            public uint Silver { get; set; }
            public uint ConquerPoints { get; set; }
            public bool Accepted { get; set; }
        }

        public Offer First { get; }
        public Offer Second { get; }

        public Trade(Client first, Client second)
        {
            this.First = new Offer { Player = first };
            this.Second = new Offer { Player = second };
        }

        public Offer Of(Client player) => player == this.First.Player ? this.First : this.Second;

        public Offer PartnerOf(Client player) => player == this.First.Player ? this.Second : this.First;

        public bool AnyAccepted => this.First.Accepted || this.Second.Accepted;
    }
}

namespace Comet.Game.Nobility
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // The number is also the battle power the rank adds, and with the gender the section of
    // the client's ini/peerage.ini (rank * 10 + 1 male, 2 female)
    public enum NobilityRank : uint
    {
        Commoner = 0,
        Knight = 1,
        Baron = 3,
        Earl = 5,
        Duke = 7,
        Prince = 9,
        King = 12,
    }

    public sealed record Donor(uint ID, string Name, uint Mesh, ulong Donation);

    public sealed record Standing(NobilityRank Rank, int Position);

    // The donation ranking. The top 50 donors are on the board: 1-3 are kings, 4-15 princes,
    // 16-50 dukes. Everyone else is ranked by their donation alone.
    public static class NobilityBoard
    {
        public const int Size = 50;
        public const int PageSize = 10;
        public const int Kings = 3, Princes = 15, Dukes = Size;
        public const ulong EarlDonation = 200_000_000, BaronDonation = 100_000_000, KnightDonation = 30_000_000;

        // The client's own minimum, and its price of a CP in silver
        public const ulong MinDonation = 3_000_000;
        public const ulong SilverPerConquerPoint = 50_000;

        public static readonly Standing Commoner = new Standing(NobilityRank.Commoner, -1);

        public static List<Donor> Order(IEnumerable<Donor> donors) =>
            donors.Where(d => d.Donation > 0)
                .OrderByDescending(d => d.Donation)
                .ThenBy(d => d.ID)
                .ToList();

        // Position counts from 0 and is -1 off the board
        public static Dictionary<uint, Standing> Rank(IReadOnlyList<Donor> ordered)
        {
            var standings = new Dictionary<uint, Standing>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
                standings[ordered[i].ID] = new Standing(RankAt(i, ordered[i].Donation), i < Size ? i : -1);
            return standings;
        }

        public static NobilityRank RankAt(int position, ulong donation) =>
            position >= 0 && position < Kings ? NobilityRank.King :
            position >= 0 && position < Princes ? NobilityRank.Prince :
            position >= 0 && position < Dukes ? NobilityRank.Duke :
            ByDonation(donation);

        public static NobilityRank ByDonation(ulong donation) =>
            donation >= EarlDonation ? NobilityRank.Earl :
            donation >= BaronDonation ? NobilityRank.Baron :
            donation >= KnightDonation ? NobilityRank.Knight :
            NobilityRank.Commoner;

        // The total donation that would give a donor the rank: for the board's ranks one more
        // than the last donor who holds a place of that rank now, not counting the donor
        public static ulong TotalFor(NobilityRank rank, IReadOnlyList<Donor> ordered, uint id)
        {
            int places = rank switch
            {
                NobilityRank.King => Kings,
                NobilityRank.Prince => Princes,
                NobilityRank.Duke => Dukes,
                _ => 0,
            };
            if (places == 0)
                return rank switch
                {
                    NobilityRank.Earl => EarlDonation,
                    NobilityRank.Baron => BaronDonation,
                    NobilityRank.Knight => KnightDonation,
                    _ => 0,
                };
            var others = ordered.Where(d => d.ID != id).ToList();
            return others.Count < places ? MinDonation : others[places - 1].Donation + 1;
        }

        // What a donor still has to give to reach the total: nothing once there, and never
        // less than one donation
        public static ulong Remaining(ulong total, ulong donation) =>
            donation >= total ? 0 : Math.Max(MinDonation, total - donation);

        // The place the donor would take with the total, or -1 off the board
        public static int PositionWith(ulong total, IReadOnlyList<Donor> ordered, uint id)
        {
            int above = ordered.Count(d => d.ID != id && d.Donation >= total);
            return above < Size ? above : -1;
        }

        public static int Pages(int donors) => (int)Math.Ceiling(Math.Min(donors, Size) / (double)PageSize);

        // CPs paid instead of silver, rounded up
        public static ulong ConquerPointsFor(ulong silver) =>
            (silver + SilverPerConquerPoint - 1) / SilverPerConquerPoint;
    }
}

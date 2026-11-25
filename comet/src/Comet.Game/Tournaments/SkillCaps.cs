namespace Comet.Game.Tournaments
{
    using System;
    using System.Collections.Concurrent;
    using Comet.Game.Items;
    using Comet.Game.Nobility;
    using Comet.Game.States;

    // The Skill Team PK Tournament's limits, on its waiting rooms and arenas: worn gear counts
    // as at most level 120, +4 and one socket (Super is the best quality anyway); nobility as at
    // most Earl; guild battle power doesn't count; a sub-class other than the Apothecary counts
    // as at most phase 5. Refinery effects stay.
    public static class SkillCaps
    {
        public const int MaxItemLevel = 120;
        public const byte MaxPlus = 4;
        public const byte MaxSubClassPhase = 5;
        public const NobilityRank MaxRank = NobilityRank.Earl;

        private static readonly ConcurrentDictionary<object, byte> Maps = new ConcurrentDictionary<object, byte>();

        public static void Add(object map) => Maps[map] = 0;

        public static void Remove(object map) => Maps.TryRemove(map, out _);

        public static bool Applies(Client client) => client?.Map is { } map && Maps.ContainsKey(map);

        public static bool Applies(uint characterID) =>
            Kernel.Players.TryGet(characterID, out var client) && Applies(client);

        public static byte PlusOf(byte plus) => Math.Min(plus, MaxPlus);

        public static NobilityRank RankOf(NobilityRank rank) => rank > MaxRank ? MaxRank : rank;

        public static uint TypeOf(uint typeID) => TypeOf(typeID, Kernel.ItemTypes);

        // The same gear at the highest level of its upgrade line up to level 120
        public static uint TypeOf(uint typeID, ItemTypeTable types)
        {
            if (!types.TryGet(typeID, out var type) || type.RequiredLevel <= MaxItemLevel)
                return typeID;
            var lowest = Upgrading.LowestLevel(typeID, types);
            if (lowest == null || lowest.RequiredLevel > MaxItemLevel)
                return typeID;
            uint best = lowest.ID;
            for (uint? next = Upgrading.NextLevelID(lowest.ID); next is uint n && n < typeID; next = Upgrading.NextLevelID(n))
                if (types.TryGet(n, out var step) && step.RequiredLevel <= MaxItemLevel)
                    best = n;
            return best;
        }
    }
}

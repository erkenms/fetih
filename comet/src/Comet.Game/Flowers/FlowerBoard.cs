namespace Comet.Game.Flowers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Items;

    public enum FlowerKind : uint
    {
        RedRose = 0,
        Lily = 1,
        Orchid = 2,
        Tulip = 3
    }

    public sealed record FlowerRecord(
        uint CharacterID,
        string Name,
        uint RedRoses,
        uint RedRosesToday,
        uint Lilies,
        uint LiliesToday,
        uint Orchids,
        uint OrchidsToday,
        uint Tulips,
        uint TulipsToday,
        DateTime? TodayDate)
    {
        public uint TotalOf(FlowerKind kind) => kind switch
        {
            FlowerKind.RedRose => this.RedRoses,
            FlowerKind.Lily => this.Lilies,
            FlowerKind.Orchid => this.Orchids,
            FlowerKind.Tulip => this.Tulips,
            _ => 0
        };

        public uint TodayOf(FlowerKind kind, DateTime today) =>
            this.TodayDate == today.Date ? kind switch
            {
                FlowerKind.RedRose => this.RedRosesToday,
                FlowerKind.Lily => this.LiliesToday,
                FlowerKind.Orchid => this.OrchidsToday,
                FlowerKind.Tulip => this.TulipsToday,
                _ => 0
            } : 0;

        public FlowerRecord Add(FlowerKind kind, uint amount, DateTime today)
        {
            var day = today.Date;
            bool sameDay = this.TodayDate == day;
            uint redToday = sameDay ? this.RedRosesToday : 0;
            uint lilyToday = sameDay ? this.LiliesToday : 0;
            uint orchidToday = sameDay ? this.OrchidsToday : 0;
            uint tulipToday = sameDay ? this.TulipsToday : 0;

            return kind switch
            {
                FlowerKind.RedRose => this with
                {
                    RedRoses = SaturatingAdd(this.RedRoses, amount),
                    RedRosesToday = SaturatingAdd(redToday, amount),
                    LiliesToday = lilyToday,
                    OrchidsToday = orchidToday,
                    TulipsToday = tulipToday,
                    TodayDate = day
                },
                FlowerKind.Lily => this with
                {
                    Lilies = SaturatingAdd(this.Lilies, amount),
                    RedRosesToday = redToday,
                    LiliesToday = SaturatingAdd(lilyToday, amount),
                    OrchidsToday = orchidToday,
                    TulipsToday = tulipToday,
                    TodayDate = day
                },
                FlowerKind.Orchid => this with
                {
                    Orchids = SaturatingAdd(this.Orchids, amount),
                    RedRosesToday = redToday,
                    LiliesToday = lilyToday,
                    OrchidsToday = SaturatingAdd(orchidToday, amount),
                    TulipsToday = tulipToday,
                    TodayDate = day
                },
                FlowerKind.Tulip => this with
                {
                    Tulips = SaturatingAdd(this.Tulips, amount),
                    RedRosesToday = redToday,
                    LiliesToday = lilyToday,
                    OrchidsToday = orchidToday,
                    TulipsToday = SaturatingAdd(tulipToday, amount),
                    TodayDate = day
                },
                _ => this
            };
        }

        private static uint SaturatingAdd(uint a, uint b) =>
            uint.MaxValue - a < b ? uint.MaxValue : a + b;
    }

    public sealed record FlowerEntry(uint CharacterID, string Name, uint Amount, int Place);

    public static class FlowerBoard
    {
        public const int TopLimit = 100;
        public const int PageSize = 10;

        public const uint RedRoseRankType = 30000002;
        public const uint LilyRankType = 30000102;
        public const uint OrchidRankType = 30000202;
        public const uint TulipRankType = 30000302;

        public static readonly FlowerKind[] AllKinds =
        {
            FlowerKind.RedRose,
            FlowerKind.Lily,
            FlowerKind.Orchid,
            FlowerKind.Tulip
        };

        public static uint RankTypeOf(FlowerKind kind) => kind switch
        {
            FlowerKind.RedRose => RedRoseRankType,
            FlowerKind.Lily => LilyRankType,
            FlowerKind.Orchid => OrchidRankType,
            FlowerKind.Tulip => TulipRankType,
            _ => 0
        };

        public static bool TryKindOf(uint rankType, out FlowerKind kind)
        {
            switch (rankType)
            {
                case RedRoseRankType: kind = FlowerKind.RedRose; return true;
                case LilyRankType: kind = FlowerKind.Lily; return true;
                case OrchidRankType: kind = FlowerKind.Orchid; return true;
                case TulipRankType: kind = FlowerKind.Tulip; return true;
                default: kind = default; return false;
            }
        }

        // Matches ini/Dynarank.ini and CMsgRank::Process: rankType + place * 10000 (1-based place).
        public static uint CharmId(FlowerKind kind, int place)
        {
            if (place < 1 || place > TopLimit)
                return 0;
            return RankTypeOf(kind) + (uint)place * 10000u;
        }

        // Daily free flower allowance by VIP level (from client STR_VIP_FLOWER_1..7).
        public static uint DailyAllowance(uint vipLevel) => vipLevel switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 5,
            4 => 10,
            5 => 15,
            _ => 30
        };

        // Flower items are 751xxx (Red Rose), 752xxx (Lily), 753xxx (Orchid), 754xxx (Tulip);
        // amount comes from itemtype.dat's Amount field (or the low 3 digits if unloaded).
        public static bool TryParseFlowerItem(uint typeId, ItemTypeTable itemTypes, out FlowerKind kind, out uint amount)
        {
            uint group = typeId / 1000;
            if (group < 751 || group > 754)
            {
                kind = default;
                amount = 0;
                return false;
            }
            kind = (FlowerKind)(group - 751);
            if (itemTypes != null && itemTypes.TryGet(typeId, out var type) && type.Amount > 0)
                amount = type.Amount;
            else
                amount = typeId % 1000;
            return amount > 0;
        }

        public static List<FlowerEntry> Order(IEnumerable<FlowerRecord> records, FlowerKind kind) =>
            records
                .Where(r => r.TotalOf(kind) > 0)
                .OrderByDescending(r => r.TotalOf(kind))
                .ThenBy(r => r.CharacterID)
                .Take(TopLimit)
                .Select((r, i) => new FlowerEntry(r.CharacterID, r.Name, r.TotalOf(kind), i + 1))
                .ToList();

        public static List<FlowerEntry> Page(IReadOnlyList<FlowerEntry> ranked, int page)
        {
            if (page < 0 || ranked == null)
                return new List<FlowerEntry>();
            int start = page * PageSize;
            if (start >= ranked.Count)
                return new List<FlowerEntry>();
            int count = Math.Min(PageSize, ranked.Count - start);
            var slice = new List<FlowerEntry>(count);
            for (int i = 0; i < count; i++)
                slice.Add(ranked[start + i]);
            return slice;
        }

        // Picks the best charm ID across all flower ranks: lowest 1-based place wins, breaking
        // ties in the same order as the client's CHero::SetBestRankID (Lily, Orchid, Tulip, RedRose).
        public static uint BestCharmId(IReadOnlyDictionary<FlowerKind, int> places)
        {
            int bestPlace = int.MaxValue;
            int bestPriority = int.MaxValue;
            uint bestCharm = 0;
            foreach (var (kind, place) in places)
            {
                if (place < 1 || place > TopLimit)
                    continue;
                int priority = TiePriority(kind);
                if (place < bestPlace || (place == bestPlace && priority < bestPriority))
                {
                    bestPlace = place;
                    bestPriority = priority;
                    bestCharm = CharmId(kind, place);
                }
            }
            return bestCharm;
        }

        private static int TiePriority(FlowerKind kind) => kind switch
        {
            FlowerKind.Lily => 1,
            FlowerKind.Orchid => 2,
            FlowerKind.Tulip => 3,
            FlowerKind.RedRose => 4,
            _ => 5
        };
    }
}

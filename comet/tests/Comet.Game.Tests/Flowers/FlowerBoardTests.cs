namespace Comet.Game.Tests.Flowers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Flowers;
    using Comet.Game.Items;
    using Xunit;

    public class FlowerBoardTests
    {
        [Theory]
        [InlineData(0u, 1u)]
        [InlineData(1u, 2u)]
        [InlineData(2u, 3u)]
        [InlineData(3u, 5u)]
        [InlineData(4u, 10u)]
        [InlineData(5u, 15u)]
        [InlineData(6u, 30u)]
        [InlineData(7u, 30u)]
        public void DailyAllowanceMatchesVipLevel(uint vip, uint expected) =>
            Assert.Equal(expected, FlowerBoard.DailyAllowance(vip));

        [Theory]
        [InlineData(751001u, FlowerKind.RedRose, 1u)]
        [InlineData(751009u, FlowerKind.RedRose, 9u)]
        [InlineData(751999u, FlowerKind.RedRose, 999u)]
        [InlineData(752003u, FlowerKind.Lily, 3u)]
        [InlineData(753099u, FlowerKind.Orchid, 99u)]
        [InlineData(754001u, FlowerKind.Tulip, 1u)]
        public void FlowerItemsParseKindAndAmount(uint typeId, FlowerKind expectedKind, uint expectedAmount)
        {
            var table = new ItemTypeTable(new[] { new ItemType { ID = typeId, Amount = (ushort)expectedAmount } });
            Assert.True(FlowerBoard.TryParseFlowerItem(typeId, table, out var kind, out var amount));
            Assert.Equal((expectedKind, expectedAmount), (kind, amount));
        }

        [Theory]
        [InlineData(755001u)] // Kiss (gift, not flower)
        [InlineData(756001u)] // LoveLetter
        [InlineData(720367u)] // QuestItem Roses
        [InlineData(751000u)] // Zero amount
        public void NonFlowerItemsAreRejected(uint typeId)
        {
            Assert.False(FlowerBoard.TryParseFlowerItem(typeId, null, out _, out _));
        }

        [Fact]
        public void OrderRanksByAmountDescendingAndCapsAtTop100()
        {
            var records = Enumerable.Range(1, 105)
                .Select(i => new FlowerRecord((uint)i, "Girl" + i, (uint)(i * 10), 0, 0, 0, 0, 0, 0, 0, null))
                .Append(new FlowerRecord(999, "Zero", 0, 0, 0, 0, 0, 0, 0, 0, null));

            var ordered = FlowerBoard.Order(records, FlowerKind.RedRose);
            Assert.Equal(100, ordered.Count);
            Assert.Equal((105u, 1050u, 1), (ordered[0].CharacterID, ordered[0].Amount, ordered[0].Place));
            Assert.Equal((6u, 60u, 100), (ordered[99].CharacterID, ordered[99].Amount, ordered[99].Place));
        }

        [Fact]
        public void EqualCountsBreakTiesByCharacterId()
        {
            var records = new[]
            {
                new FlowerRecord(20, "B", 50, 0, 0, 0, 0, 0, 0, 0, null),
                new FlowerRecord(10, "A", 50, 0, 0, 0, 0, 0, 0, 0, null),
            };
            var ordered = FlowerBoard.Order(records, FlowerKind.RedRose);
            Assert.Equal(new uint[] { 10, 20 }, ordered.Select(e => e.CharacterID));
        }

        [Fact]
        public void PagingReturnsTenPerPage()
        {
            var ranked = Enumerable.Range(1, 23)
                .Select(i => new FlowerEntry((uint)i, "Girl" + i, (uint)(100 - i), i))
                .ToList();

            var p0 = FlowerBoard.Page(ranked, 0);
            var p2 = FlowerBoard.Page(ranked, 2);
            var p3 = FlowerBoard.Page(ranked, 3);

            Assert.Equal(10, p0.Count);
            Assert.Equal(1, p0[0].Place);
            Assert.Equal(3, p2.Count);
            Assert.Equal(21, p2[0].Place);
            Assert.Empty(p3);
        }

        [Theory]
        [InlineData(FlowerKind.RedRose, 1, 30010002u)]
        [InlineData(FlowerKind.RedRose, 30, 30300002u)]
        [InlineData(FlowerKind.Lily, 1, 30010102u)]
        [InlineData(FlowerKind.Orchid, 2, 30020202u)]
        [InlineData(FlowerKind.Tulip, 10, 30100302u)]
        [InlineData(FlowerKind.RedRose, 0, 0u)]
        [InlineData(FlowerKind.RedRose, 101, 0u)]
        public void CharmIdMatchesDynarankIni(FlowerKind kind, int place, uint expected) =>
            Assert.Equal(expected, FlowerBoard.CharmId(kind, place));

        [Fact]
        public void BestCharmPicksLowestPlaceAndBreaksTiesLikeClient()
        {
            Assert.Equal(30020302u, FlowerBoard.BestCharmId(new Dictionary<FlowerKind, int>
            {
                [FlowerKind.RedRose] = 5,
                [FlowerKind.Tulip] = 2
            }));
            // Equal place: Lily (1) beats Orchid (2), Tulip (3), RedRose (4)
            Assert.Equal(30010102u, FlowerBoard.BestCharmId(new Dictionary<FlowerKind, int>
            {
                [FlowerKind.RedRose] = 1,
                [FlowerKind.Lily] = 1,
                [FlowerKind.Orchid] = 1,
                [FlowerKind.Tulip] = 1
            }));
        }

        [Fact]
        public void DailyCountsResetOnNewDayWhileTotalsPersist()
        {
            var day1 = new DateTime(2026, 11, 24);
            var day2 = new DateTime(2026, 11, 25);

            var r = new FlowerRecord(1, "Rose", 0, 0, 0, 0, 0, 0, 0, 0, null)
                .Add(FlowerKind.RedRose, 9, day1)
                .Add(FlowerKind.Lily, 3, day1);

            Assert.Equal((9u, 9u), (r.RedRoses, r.TodayOf(FlowerKind.RedRose, day1)));
            Assert.Equal((3u, 3u), (r.Lilies, r.TodayOf(FlowerKind.Lily, day1)));

            // On day2 before receiving anything, today counts read as 0
            Assert.Equal(0u, r.TodayOf(FlowerKind.RedRose, day2));
            Assert.Equal(0u, r.TodayOf(FlowerKind.Lily, day2));

            // Receiving on day2 clears yesterday's today counts and keeps totals
            var r2 = r.Add(FlowerKind.Tulip, 1, day2);
            Assert.Equal((9u, 0u), (r2.RedRoses, r2.RedRosesToday));
            Assert.Equal((3u, 0u), (r2.Lilies, r2.LiliesToday));
            Assert.Equal((1u, 1u), (r2.Tulips, r2.TulipsToday));
            Assert.Equal(day2, r2.TodayDate);
        }
    }
}

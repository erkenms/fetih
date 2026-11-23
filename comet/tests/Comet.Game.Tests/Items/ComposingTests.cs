namespace Comet.Game.Tests.Items
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Xunit;

    public class ComposingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

        private static DbItem Item(uint type, byte plus, ushort amount = 100) =>
            new DbItem { TypeID = type, Plus = plus, Amount = amount, AmountLimit = 100 };

        [Theory]
        [InlineData(1, 10u)]
        [InlineData(2, 40u)]
        [InlineData(8, 29160u)]
        [InlineData(9, 0u)]
        public void MinorsGiveThePointsOfTheirPlus(byte plus, uint points) =>
            Assert.Equal(points, Composing.PointsOf(plus));

        [Fact]
        public void TwoPlusOnesMakeAPlusOne()
        {
            var (plus, progress) = Composing.Add(0, 0, Composing.PointsOf(1));
            Assert.Equal((0, 10u), (plus, progress));
            Assert.Equal((1, 0u), Composing.Add(plus, progress, Composing.PointsOf(1)));
        }

        [Fact]
        public void PointsCarryOverSeveralLevels()
        {
            // +0 needs 20, +1 20, +2 80: a +3's 120 reaches +3 exactly
            Assert.Equal((3, 0u), Composing.Add(0, 0, Composing.PointsOf(3)));
            // A +8 from +0: 20 + 20 + 80 + 240 + 720 + 2160 + 6480 + 19440 = 29160, so +8
            Assert.Equal((8, 0u), Composing.Add(0, 0, Composing.PointsOf(8)));
        }

        [Fact]
        public void PastNineLevelsNeedFewerPoints()
        {
            // A +8 needs two +8 minors for +9, but one takes a +9 past 2700, 5500 and 9000 to +12
            Assert.Equal((9, 0u), Composing.Add(8, 29160, Composing.PointsOf(8)));
            Assert.Equal((12, 0u), Composing.Add(9, 0, Composing.PointsOf(8)));
            Assert.Equal((11, 100u), Composing.Add(9, 0, 2700 + 5500 + 100));
        }

        [Fact]
        public void PlusStopsAtTwelve()
        {
            Assert.Equal((12, 0u), Composing.Add(11, 8000, Composing.PointsOf(8)));
            Assert.Equal("main_at_max", Composing.Refusal(Item(111003, 12), Item(111013, 1), Now));
        }

        [Fact]
        public void MinorsAreGearOfTheSameKindOrStones()
        {
            var helmet = Item(111003, 0);
            Assert.Null(Composing.Refusal(helmet, Item(111013, 1), Now));
            Assert.Null(Composing.Refusal(helmet, Item(730003, 0), Now));
            Assert.Equal("minor_kind", Composing.Refusal(helmet, Item(410301, 1), Now));
            Assert.Equal("minor_plus", Composing.Refusal(helmet, Item(111013, 0), Now));
            Assert.Equal("minor_plus", Composing.Refusal(helmet, Item(111013, 9), Now));
            Assert.Equal("minor_broken", Composing.Refusal(helmet, Item(111013, 1, amount: 0), Now));
            var locked = Item(111013, 1);
            locked.Locked = true;
            Assert.Equal("minor_locked", Composing.Refusal(helmet, locked, Now));
            Assert.Equal("main_not_composable", Composing.Refusal(Item(1050020, 0), Item(730001, 0), Now));
        }

        [Fact]
        public void WeaponsComposeWithWeaponsButNotShields()
        {
            Assert.Null(Composing.Refusal(Item(410301, 0), Item(510301, 1), Now));   // blade and glaive
            Assert.Null(Composing.Refusal(Item(500301, 0), Item(601301, 1), Now));   // bow and katana
            Assert.Equal("minor_kind", Composing.Refusal(Item(410301, 0), Item(900301, 1), Now));
        }
    }
}

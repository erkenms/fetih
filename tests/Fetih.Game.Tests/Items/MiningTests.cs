namespace Fetih.Game.Tests.Items
{
    using System;
    using System.Linq;
    using Fetih.Game.Items;
    using Xunit;

    public class MiningTests
    {
        [Fact]
        public void DigsTurnUpOresOfTheFourKindsAndRareGems()
        {
            var random = new Random(11);
            var finds = Enumerable.Range(0, 20000).Select(_ => Mining.Find(random)).ToList();
            var found = finds.Where(f => f != null).Select(f => f.Value).ToList();
            Assert.InRange(found.Count, 20000 * 25 / 100, 20000 * 35 / 100);
            Assert.All(found, type => Assert.True(
                type is >= 1072010 and <= 1072029 or >= 1072040 and <= 1072059 ||
                type is >= 700001 and <= 700071 && type % 10 == 1, $"type {type}"));
            Assert.Contains(found, t => t / 10 == 107201);
            Assert.Contains(found, t => t / 10 == 107205);
            Assert.Contains(found, t => t / 1000 == 700);
            Assert.True(found.Count(t => t / 10 == 107201) > found.Count(t => t / 10 == 107205));
        }

        [Fact]
        public void LowGradesAreCommoner()
        {
            var random = new Random(3);
            var grades = Enumerable.Range(0, 11000).Select(_ => Mining.Grade(random)).ToList();
            Assert.All(grades, g => Assert.InRange(g, 1u, 10u));
            Assert.True(grades.Count(g => g == 1) > 3 * grades.Count(g => g == 10));
        }

        [Fact]
        public void OnlyPickAxesDig()
        {
            Assert.True(Mining.IsPick(562000));
            Assert.False(Mining.IsPick(410301));
        }
    }
}

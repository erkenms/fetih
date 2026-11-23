namespace Comet.Game.Tests.Items
{
    using System;
    using Comet.Game.Items;
    using Xunit;

    public class EnchantingTests
    {
        // help44's table, by socket value: kind * 10 + grade
        [Theory]
        [InlineData(11, 1, 59)]      // any normal gem
        [InlineData(71, 1, 59)]
        [InlineData(12, 100, 159)]   // dragon
        [InlineData(13, 200, 255)]
        [InlineData(32, 80, 129)]    // rainbow
        [InlineData(33, 170, 229)]
        [InlineData(2, 60, 109)]     // phoenix
        [InlineData(3, 170, 229)]
        [InlineData(72, 40, 89)]     // tortoise
        [InlineData(73, 170, 229)]
        [InlineData(62, 60, 109)]    // moon
        [InlineData(63, 140, 199)]
        [InlineData(52, 60, 109)]    // violet
        [InlineData(53, 140, 199)]
        [InlineData(22, 40, 89)]     // fury
        [InlineData(23, 90, 149)]
        [InlineData(42, 40, 89)]     // kylin
        [InlineData(43, 70, 119)]
        public void GemsRollInTheirRange(byte gem, int min, int max) =>
            Assert.Equal((min, max), Enchanting.Range(gem));

        [Theory]
        [InlineData(103)]   // thunder
        [InlineData(123)]   // glory
        [InlineData(0)]
        [InlineData(255)]
        public void OtherGemsDontEnchant(byte gem) => Assert.Null(Enchanting.Range(gem));

        [Fact]
        public void RollsStayInRange()
        {
            var random = new Random(7);
            for (int i = 0; i < 1000; i++)
                Assert.InRange(Enchanting.Roll(13, random), 200, 255);
        }
    }
}

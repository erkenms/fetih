namespace Fetih.Game.Tests.Magic
{
    using Fetih.Game.Magic;
    using Xunit;

    public class PoisonTests
    {
        [Theory]
        [InlineData(33u, 3u)]
        [InlineData(5u, 1u)]
        [InlineData(2u, 1u)]
        [InlineData(1u, 0u)]
        [InlineData(0u, 0u)]
        public void APoisonTakesItsShareButNeverTheLastPoint(uint health, uint taken) =>
            Assert.Equal(taken, new Poisoning { Percent = 10 }.Take(health));
    }
}

namespace Fetih.Game.Tests.World
{
    using System;
    using Fetih.Game.World;
    using Xunit;

    public class JumpTimeTests
    {
        [Theory]
        // Short jumps play at 33 ms a frame
        [InlineData(6, false, false, false, 660)]
        // Sixteen cells along x are 572 world units, 63 ms a frame
        [InlineData(16, false, false, false, 1260)]
        [InlineData(16, true, false, false, 620)]
        [InlineData(16, false, true, false, 840)]
        [InlineData(16, true, true, false, 620)]
        [InlineData(16, false, false, true, 2520)]
        public void FollowsTheClientsFrames(int cells, bool cyclone, bool oblivion, bool flying, int ms) =>
            Assert.Equal(TimeSpan.FromMilliseconds(ms), Movement.JumpTime(100, 100, 100 + cells, 100, cyclone, oblivion, flying));
    }
}

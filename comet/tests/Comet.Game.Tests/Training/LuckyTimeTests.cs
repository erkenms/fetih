namespace Comet.Game.Tests.Training
{
    using System;
    using Comet.Game.Training;
    using Xunit;

    public class LuckyTimeTests
    {
        [Fact]
        public void PrayingGainsThreeTimesAbsorbingOnceOtherwiseItRunsDown()
        {
            var second = TimeSpan.FromSeconds(1);
            Assert.Equal(3_000u, LuckyTime.Accrue(0, praying: true, absorbing: false, second));
            Assert.Equal(1_000u, LuckyTime.Accrue(0, praying: false, absorbing: true, second));
            Assert.Equal(4_000u, LuckyTime.Accrue(5_000, praying: false, absorbing: false, second));
        }

        [Fact]
        public void LuckyTimeStaysBetweenZeroAndTwoHours()
        {
            Assert.Equal(0u, LuckyTime.Accrue(500, praying: false, absorbing: false, TimeSpan.FromSeconds(1)));
            Assert.Equal(LuckyTime.MaxMilliseconds,
                LuckyTime.Accrue(LuckyTime.MaxMilliseconds - 10, praying: true, absorbing: false, TimeSpan.FromSeconds(1)));
            Assert.Equal(7_200_000u, LuckyTime.MaxMilliseconds);
        }
    }
}

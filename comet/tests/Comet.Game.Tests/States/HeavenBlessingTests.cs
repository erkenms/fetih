namespace Comet.Game.Tests.States
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Xunit;

    public class HeavenBlessingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void StonesGiveThreeSevenOrThirtyDays()
        {
            Assert.Equal(TimeSpan.FromDays(3), HeavenBlessing.StoneDuration(1200000));
            Assert.Equal(TimeSpan.FromDays(7), HeavenBlessing.StoneDuration(1200001));
            Assert.Equal(TimeSpan.FromDays(30), HeavenBlessing.StoneDuration(1200002));
            Assert.Null(HeavenBlessing.StoneDuration(1200005));
        }

        [Fact]
        public void MoreTimeAddsToWhatIsLeftOrStartsNow()
        {
            var days = TimeSpan.FromDays(3);
            Assert.Equal(Now + days, HeavenBlessing.Extend(null, days, Now));
            Assert.Equal(Now + days, HeavenBlessing.Extend(Now.AddDays(-1), days, Now));
            Assert.Equal(Now.AddDays(5), HeavenBlessing.Extend(Now.AddDays(2), days, Now));
        }

        [Fact]
        public void SecondsLeftCountDownToZero()
        {
            var character = new DbCharacter(1, "Blessed") { HeavenBlessing = Now.AddSeconds(90.5) };
            Assert.True(HeavenBlessing.IsBlessed(character, Now));
            Assert.Equal(91u, HeavenBlessing.SecondsLeft(character, Now));
            Assert.False(HeavenBlessing.IsBlessed(character, Now.AddSeconds(91)));
            Assert.Equal(0u, HeavenBlessing.SecondsLeft(character, Now.AddSeconds(91)));
            Assert.False(HeavenBlessing.IsBlessed(new DbCharacter(1, "Plain"), Now));
        }
    }
}

namespace Comet.Game.Tests.States
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Xunit;

    public class HeavenBlessingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(1200000u, 3)]
        [InlineData(1200001u, 7)]
        [InlineData(1200002u, 30)]
        [InlineData(1200005u, null)]
        public void StonesGiveThreeSevenOrThirtyDays(uint type, int? days) =>
            Assert.Equal(days.HasValue ? TimeSpan.FromDays(days.Value) : null, HeavenBlessing.StoneDuration(type));

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

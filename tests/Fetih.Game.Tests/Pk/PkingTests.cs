namespace Fetih.Game.Tests.Pk
{
    using System;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Pk;
    using Fetih.Game.States;
    using Xunit;

    public class PkingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static Character Hero(ushort points) =>
            new Character(new DbCharacter(1, 1000001, "Hero") { KillPoints = points });

        [Theory]
        [InlineData(0, 0UL)]
        [InlineData(29, 0UL)]
        [InlineData(30, Pking.RedNameStatus)]
        [InlineData(99, Pking.RedNameStatus)]
        [InlineData(100, Pking.BlackNameStatus)]
        [InlineData(5000, Pking.BlackNameStatus)]
        public void NameColorFollowsPkPoints(ushort points, ulong status) =>
            Assert.Equal(status, Pking.NameStatus(points));

        [Theory]
        [InlineData(PkMode.Free, false, true, true)]
        [InlineData(PkMode.Peace, true, false, false)]
        [InlineData(PkMode.Team, false, false, true)]
        [InlineData(PkMode.Team, true, true, false)]
        [InlineData(PkMode.Capture, false, false, false)]
        [InlineData(PkMode.Capture, true, true, true)]
        public void ModesDecideWhoMayBeAttacked(PkMode mode, bool guilty, bool ally, bool permitted) =>
            Assert.Equal(permitted, Pking.Permits(mode, guilty, ally));

        [Fact]
        public void FlashingStartsOnceAndRunsOut()
        {
            var state = new PkState();
            var hero = Hero(30);
            Assert.True(state.Flash(Now, TimeSpan.FromSeconds(30)));
            Assert.False(state.Flash(Now.AddSeconds(10), TimeSpan.FromSeconds(30)));
            Assert.Equal(Pking.RedNameStatus | Pking.FlashingStatus, state.Status(hero, Now.AddSeconds(39)));

            Assert.False(state.StopFlashing(Now.AddSeconds(39)));
            Assert.True(state.StopFlashing(Now.AddSeconds(40)));
            Assert.False(state.StopFlashing(Now.AddSeconds(41)));
            Assert.Equal(Pking.RedNameStatus, state.Status(hero, Now.AddSeconds(41)));
        }

        [Fact]
        public void DyingEndsFlashingButKeepsTheNameColor()
        {
            var state = new PkState();
            var hero = Hero(30);
            state.Flash(Now, TimeSpan.FromSeconds(30));
            Assert.True(state.ClearFlashing(Now.AddSeconds(5)));
            Assert.False(state.IsFlashing(Now.AddSeconds(5)));
            Assert.Equal(Pking.RedNameStatus, state.Status(hero, Now.AddSeconds(5)));
            Assert.False(state.ClearFlashing(Now.AddSeconds(6)));
            Assert.False(state.StopFlashing(Now.AddSeconds(40)));
        }

        [Fact]
        public void PointsDecayOncePerIntervalAndStopAtZero()
        {
            var state = new PkState();
            var hero = Hero(1);
            var interval = TimeSpan.FromMinutes(6);
            Assert.False(state.Decay(hero, Now, interval, out _));
            Assert.False(state.Decay(hero, Now.AddMinutes(5), interval, out _));
            Assert.True(state.Decay(hero, Now.AddMinutes(6), interval, out var points));
            Assert.Equal(0, points);
            Assert.False(state.Decay(hero, Now.AddMinutes(12), interval, out points));
            Assert.Equal(0, hero.KillPoints);
        }

        [Fact]
        public void PointsStayInRange()
        {
            var state = new PkState();
            var hero = Hero(5);
            Assert.Equal(((ushort)5, (ushort)0), state.ChangePoints(hero, -10));
            Assert.Equal(((ushort)0, ushort.MaxValue), state.ChangePoints(hero, 100_000));
        }

        [Fact]
        public void PlayersStartInCaptureMode() => Assert.Equal(PkMode.Capture, new PkState().Mode);
    }
}

namespace Fetih.Game.Tests
{
    using System;
    using Xunit;

    public class ClockTests
    {
        [Fact]
        public void TheDayTurnsAtTheZonesMidnight()
        {
            var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
            var lateEvening = new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc);
            Assert.Equal(new DateTime(2026, 11, 21, 1, 0, 0), Clock.Local(lateEvening, istanbul));
            Assert.Equal(new DateTime(2026, 11, 20), Clock.Local(lateEvening, TimeZoneInfo.Utc).Date);
        }
    }
}

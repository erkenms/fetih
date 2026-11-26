namespace Fetih.Game.Tests.Events
{
    using System;
    using System.Linq;
    using Fetih.Game.Events;
    using Fetih.Game.Tests.World;
    using Xunit;

    public class EventScheduleTests
    {
        private const string Ini =
            "[tasktoday]\r\nNum=3\r\n\r\n" +
            "[t9]\r\nid=9\r\ntype=1\r\nDate=5\r\nName=Elite PK\r\nLevel_min=1\r\nLevel_max=99\r\nTimes=1\r\n" +
            "Enter1=19:55~20:06\r\nLast1=20:06\r\npic=Newactivities_FightBtn\r\n\r\n" +
            "[t15]\r\nid=15\r\ntype=1\r\nDate=6\r\nName=Capture the Flag\r\nLevel_min=1\r\nLevel_max=200\r\n" +
            "Enter1=21:00~22:00\r\nLast1=21:00~22:00\r\n\r\n" +
            "[t17]\r\nid=17\r\ntype=1\r\nDate=7\r\nName=Guild PK\r\nEnter1=00:01~20:59\r\nLast1=00:01~20:59\r\n\r\n" +
            "[t26]\r\nid=30\r\ntype=2\r\nDate=20120614-20120618\r\nName=Father`s Day\r\nEnter1=00:01~23:59\r\n";

        // 2026-11-27 is a Friday
        private static readonly DateTime Friday = new DateTime(2026, 11, 27);

        [Fact]
        public void ReadsWeeklyEventsWithTheirDaysLevelsAndWindows()
        {
            var schedule = EventSchedule.Parse(Ini);

            Assert.Equal(new[] { "Elite PK", "Capture the Flag", "Guild PK" }, schedule.All.Select(e => e.Name));
            var elite = schedule.Named("elite pk").Single();
            Assert.Equal((9, 1, 99), (elite.ID, elite.LevelMin, elite.LevelMax));
            Assert.Equal(new[] { DayOfWeek.Friday }, elite.Days);
            Assert.Equal(new EventWindow(new TimeSpan(19, 55, 0), new TimeSpan(20, 6, 0), null), elite.Windows.Single());
            Assert.Equal(new[] { DayOfWeek.Sunday }, schedule.Named("Guild PK").Single().Days);
            var ctf = schedule.Named("Capture the Flag").Single().Windows.Single();
            Assert.Equal((new TimeSpan(21, 0, 0), new TimeSpan(22, 0, 0)), (ctf.Start, ctf.End.Value));
        }

        [Fact]
        public void EdgesAreThoseBetweenTwoTicks()
        {
            var elite = EventSchedule.Parse(Ini).Named("Elite PK").Single();

            var edges = EventSchedule.Edges(elite, Friday.AddHours(19).AddMinutes(50), Friday.AddHours(21)).ToList();
            Assert.Equal(new[] { EventEdge.SignUpOpens, EventEdge.Starts }, edges.Select(e => e.Edge));
            Assert.Equal(Friday.Add(new TimeSpan(20, 6, 0)), edges[1].At);

            // A tick exactly at 19:55 opens sign-up once, and the next one doesn't again
            var at = Friday.Add(new TimeSpan(19, 55, 0));
            Assert.Single(EventSchedule.Edges(elite, at.AddSeconds(-1), at));
            Assert.Empty(EventSchedule.Edges(elite, at, at.AddSeconds(1)));
            // Not on Thursday
            Assert.Empty(EventSchedule.Edges(elite, Friday.AddDays(-1), Friday.AddDays(-1).AddHours(23)));
        }

        [Fact]
        public void SignUpIsOpenFromEnterUntilTheStart()
        {
            var elite = EventSchedule.Parse(Ini).Named("Elite PK").Single();

            Assert.Null(EventSchedule.SignUpWindow(elite, Friday.Add(new TimeSpan(19, 54, 59))));
            Assert.NotNull(EventSchedule.SignUpWindow(elite, Friday.Add(new TimeSpan(19, 55, 0))));
            Assert.NotNull(EventSchedule.SignUpWindow(elite, Friday.Add(new TimeSpan(20, 5, 59))));
            Assert.Null(EventSchedule.SignUpWindow(elite, Friday.Add(new TimeSpan(20, 6, 0))));
            Assert.Equal(Friday.Add(new TimeSpan(19, 55, 0)), EventSchedule.NextSignUp(elite, Friday.AddDays(-3)));
            Assert.Equal(Friday.AddDays(7).Add(new TimeSpan(19, 55, 0)), EventSchedule.NextSignUp(elite, Friday.AddHours(20)));
        }

        [RealClientFact]
        public void TheClientsEliteGroupsSignUpAtFiveToEightOnFridayAndStartTwoMinutesApart()
        {
            var elite = EventSchedule.Load(RealClientFactAttribute.Directory).Named("Elite PK")
                .OrderByDescending(e => e.LevelMin).ToList();

            Assert.Equal(new[] { 130, 120, 100, 1 }, elite.Select(e => e.LevelMin));
            Assert.All(elite, e => Assert.Equal(new[] { DayOfWeek.Friday }, e.Days));
            Assert.All(elite, e => Assert.Equal(new TimeSpan(19, 55, 0), e.Windows.Single().SignUp));
            Assert.Equal(new[] { 0, 2, 4, 6 }, elite.Select(e => e.Windows.Single().Start.Minutes));
        }
    }
}

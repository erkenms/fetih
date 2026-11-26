namespace Fetih.Web.Tests;

using Fetih.Web.Features.Broadcasts;
using Xunit;

public class BroadcastsTests
{
    [Fact]
    public void AnnouncementSchedulerIsDueRules()
    {
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var baseRow = new DbWebAnnouncement
        {
            ID = 1,
            Title = "Tip",
            Channel = "Center",
            Message = "Welcome!",
            IntervalMinutes = 10,
            Enabled = true,
        };

        Assert.True(AnnouncementScheduler.IsDue(baseRow, now));

        baseRow.Enabled = false;
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));
        baseRow.Enabled = true;

        baseRow.StartAt = now.AddMinutes(5);
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));
        baseRow.StartAt = now.AddMinutes(-5);

        baseRow.EndAt = now;
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));
        baseRow.EndAt = now.AddMinutes(30);

        baseRow.LastSentAt = now.AddMinutes(-5);
        Assert.False(AnnouncementScheduler.IsDue(baseRow, now));

        baseRow.LastSentAt = now.AddMinutes(-10);
        Assert.True(AnnouncementScheduler.IsDue(baseRow, now));
    }
}

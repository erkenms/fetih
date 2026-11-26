namespace Fetih.Web.Features.Broadcasts;

using System.ComponentModel.DataAnnotations.Schema;
using Fetih.Game.Database.Models;
using Microsoft.EntityFrameworkCore;

[Table("web_announcement")]
public class DbWebAnnouncement
{
    public uint ID { get; set; }
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "Center";
    public string Message { get; set; } = "";
    public uint IntervalMinutes { get; set; }
    public bool Enabled { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public DateTime? LastSentAt { get; set; }
    public uint CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BroadcastsDbContext(DbContextOptions<BroadcastsDbContext> options) : DbContext(options)
{
    public DbSet<DbWebAnnouncement> Announcements { get; set; }
    public DbSet<DbBroadcast> Queue { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DbWebAnnouncement>(e => e.HasKey(x => x.ID));
        builder.Entity<DbBroadcast>(e => e.HasKey(x => x.ID));
    }
}

namespace Comet.Web.Features.Notifications;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[Table("web_notification_webhook")]
public class DbWebNotificationWebhook
{
    [Key]
    public uint ID { get; set; }
    public string Name { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool NotifyBans { get; set; } = true;
    public bool NotifyErrors { get; set; } = true;
    public bool NotifyCheatReports { get; set; } = true;
    public uint MinIntervalSeconds { get; set; }
    public DateTime? LastSentAt { get; set; }
    public string? LastError { get; set; }
    public uint CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[Table("web_notification_delivery")]
public class DbWebNotificationDelivery
{
    [Key]
    public ulong ID { get; set; }
    public uint WebhookID { get; set; }
    public string WebhookName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public DateTime SentAt { get; set; }
}

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<DbWebNotificationWebhook> Webhooks => this.Set<DbWebNotificationWebhook>();
    public DbSet<DbWebNotificationDelivery> Deliveries => this.Set<DbWebNotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DbWebNotificationWebhook>(e => e.HasKey(x => x.ID));
        builder.Entity<DbWebNotificationDelivery>(e => e.HasKey(x => x.ID));
    }
}

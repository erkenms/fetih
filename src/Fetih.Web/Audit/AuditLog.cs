namespace Fetih.Web.Audit;

using System.Text.Json;
using Fetih.Core.Logging;
using Fetih.Web.Auth;
using Microsoft.EntityFrameworkCore;

// Every change made from the web goes through here: a web_audit row and a web.<action> event.
// Action names are snake_case verbs like "character.give_item"; the target is what changed.
public sealed class AuditLog(IDbContextFactory<WebDbContext> factory, IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(string action, string? targetType, string? targetId,
        object? before = null, object? after = null, string? summary = null)
    {
        var context = http.HttpContext;
        var user = context?.User;
        var entry = new DbWebAudit
        {
            Timestamp = DateTime.UtcNow,
            AccountID = user?.AccountId() ?? 0,
            Username = user?.Username() ?? "",
            IPAddress = context?.Connection.RemoteIpAddress?.ToString(),
            Action = action,
            TargetType = targetType,
            TargetID = targetId,
            Summary = summary,
            Before = before == null ? null : JsonSerializer.Serialize(before, Json),
            After = after == null ? null : JsonSerializer.Serialize(after, Json),
        };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Audit.Add(entry);
            await db.SaveChangesAsync();
        }

        LogEvents.For<AuditLog>(WebEvents.Audit(action))
            .ForContext("AccountId", entry.AccountID)
            .Information("Web {Action} on {TargetType} {TargetId} by {Username}: {Summary}",
                action, targetType, targetId, entry.Username, summary);
    }
}

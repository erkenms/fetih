namespace Comet.Web.Features.Audit;

using Comet.Web.Audit;
using Comet.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public record AuditEntry(ulong Id, DateTime Timestamp, uint AccountId, string Username,
    string? IpAddress, string Action, string? TargetType, string? TargetId, string? Summary,
    string? Before, string? After);

public record AuditPage(AuditEntry[] Items, int Total);

public class AuditFeature : IFeature
{
    public const int MaxPageSize = 200;

    public void Map(RouteGroupBuilder api)
    {
        api.MapGet("/audit", ListAsync).WithName("listAudit").RequirePermission(Permission.ViewAudit);
    }

    private static async Task<Ok<AuditPage>> ListAsync(IDbContextFactory<WebDbContext> factory,
        uint? accountId, string? action, string? targetType, string? targetId,
        DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        await using var db = await factory.CreateDbContextAsync();
        IQueryable<DbWebAudit> query = db.Audit.AsNoTracking();
        if (accountId != null)
            query = query.Where(x => x.AccountID == accountId);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action.StartsWith(action.Trim()));
        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(x => x.TargetType == targetType.Trim());
        if (!string.IsNullOrWhiteSpace(targetId))
            query = query.Where(x => x.TargetID == targetId.Trim());
        if (from != null)
            query = query.Where(x => x.Timestamp >= from.Value.ToUniversalTime());
        if (to != null)
            query = query.Where(x => x.Timestamp < to.Value.ToUniversalTime());

        int total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.ID)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AuditEntry(x.ID, DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc),
                x.AccountID, x.Username, x.IPAddress, x.Action, x.TargetType, x.TargetID,
                x.Summary, x.Before, x.After))
            .ToArrayAsync();
        return TypedResults.Ok(new AuditPage(items, total));
    }
}

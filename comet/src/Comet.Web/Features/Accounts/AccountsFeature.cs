namespace Comet.Web.Features.Accounts;

using System.Globalization;
using System.Security.Claims;
using Comet.Account.Database.Models;
using Comet.Account.Database.Repositories;
using Comet.Game.States;
using Comet.Shared.Models.Admin;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Features.Catalogs;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using AccountDbContext = Comet.Account.Database.ServerDbContext;
using GameDbContext = Comet.Game.Database.ServerDbContext;

public sealed class AccountsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config) { }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/accounts").RequirePermission(Permission.AdminArea);
        group.MapGet("/", ListAsync).WithName("listAccounts");
        group.MapGet("/{id:int}", GetAsync)
            .WithName("getAccount")
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/ban", BanAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("banAccount")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/unban", UnbanAsync)
            .RequirePermission(Permission.ManageCharacters)
            .WithName("unbanAccount")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/status", SetStatusAsync)
            .RequirePermission(Permission.ManageAccounts)
            .WithName("setAccountStatus")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/authority", SetAuthorityAsync)
            .RequirePermission(Permission.ManageAccounts)
            .WithName("setAccountAuthority")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        group.MapPost("/{id:int}/reset-password", ResetPasswordAsync)
            .RequirePermission(Permission.ManageAccounts)
            .WithName("resetAccountPassword")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<AccountListResponse>> ListAsync(
        string? q = null,
        ushort? status = null,
        ushort? authority = null,
        string? ip = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        await using var accountDb = new AccountDbContext();
        var query = accountDb.Accounts.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(a => a.StatusID == status.Value);
        if (authority.HasValue)
            query = query.Where(a => a.AuthorityID == authority.Value);

        string? ipFilter = string.IsNullOrWhiteSpace(ip) ? null : ip.Trim();
        if (ipFilter != null)
        {
            var loginAccountIds = accountDb.Logins.AsNoTracking()
                .Where(l => l.IPAddress == ipFilter)
                .Select(l => l.AccountID);
            query = query.Where(a => a.IPAddress == ipFilter || loginAccountIds.Contains(a.AccountID));
        }

        string? term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (term != null)
        {
            bool isId = uint.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsedId);
            var matchingLoginIds = accountDb.Logins.AsNoTracking()
                .Where(l => l.IPAddress != null && l.IPAddress.Contains(term))
                .Select(l => l.AccountID);

            query = query.Where(a =>
                (isId && a.AccountID == parsedId) ||
                a.Username.Contains(term) ||
                (a.Email != null && a.Email.Contains(term)) ||
                (a.Name != null && a.Name.Contains(term)) ||
                (a.IPAddress != null && a.IPAddress.Contains(term)) ||
                matchingLoginIds.Contains(a.AccountID));
        }

        int total = await query.CountAsync(ct);
        var accounts = await query
            .OrderByDescending(a => a.AccountID)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);

        var ids = accounts.Select(a => a.AccountID).ToArray();
        var lastLogins = ids.Length == 0
            ? new Dictionary<uint, DateTime>()
            : await accountDb.Logins.AsNoTracking()
                .Where(l => ids.Contains(l.AccountID))
                .GroupBy(l => l.AccountID)
                .Select(g => new { AccountId = g.Key, Last = g.Max(x => x.Timestamp) })
                .ToDictionaryAsync(x => x.AccountId, x => DateTime.SpecifyKind(x.Last, DateTimeKind.Utc), ct);

        await using var gameDb = new GameDbContext();
        var charCounts = ids.Length == 0
            ? new Dictionary<uint, int>()
            : await gameDb.Characters.AsNoTracking()
                .Where(c => ids.Contains(c.AccountID))
                .GroupBy(c => c.AccountID)
                .Select(g => new { AccountId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AccountId, x => x.Count, ct);

        var items = accounts.Select(a => new AccountListItem(
            a.AccountID,
            a.Username,
            a.Email,
            a.Name,
            a.AuthorityID,
            a.StatusID,
            a.IPAddress,
            DateTime.SpecifyKind(a.Registered, DateTimeKind.Utc),
            lastLogins.TryGetValue(a.AccountID, out var last) ? last : null,
            charCounts.GetValueOrDefault(a.AccountID))).ToArray();

        return TypedResults.Ok(new AccountListResponse(items, total, page, pageSize));
    }

    private static async Task<Results<Ok<AccountDetailResponse>, JsonHttpResult<ApiError>>> GetAsync(
        uint id,
        GameRpc rpc,
        CatalogStore catalogs,
        CancellationToken ct = default)
    {
        await using var accountDb = new AccountDbContext();
        var account = await accountDb.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.AccountID == id, ct);
        if (account == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        var logins = await accountDb.Logins.AsNoTracking()
            .Where(l => l.AccountID == id)
            .OrderByDescending(l => l.Timestamp)
            .Take(100)
            .Select(l => new AccountLoginEntry(
                DateTime.SpecifyKind(l.Timestamp, DateTimeKind.Utc),
                l.IPAddress))
            .ToArrayAsync(ct);

        var ips = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(account.IPAddress))
            ips.Add(account.IPAddress);
        foreach (var login in logins)
        {
            if (!string.IsNullOrWhiteSpace(login.IpAddress))
                ips.Add(login.IpAddress);
        }

        SameIpAccountSummary[] sameIp = [];
        if (ips.Count > 0)
        {
            var ipList = ips.ToArray();
            var loginHits = await accountDb.Logins.AsNoTracking()
                .Where(l => l.AccountID != id && ipList.Contains(l.IPAddress))
                .Select(l => new { l.AccountID, l.IPAddress })
                .Distinct()
                .ToArrayAsync(ct);

            var directMatches = await accountDb.Accounts.AsNoTracking()
                .Where(a => a.AccountID != id && a.IPAddress != null && ipList.Contains(a.IPAddress))
                .Select(a => new { a.AccountID, a.IPAddress })
                .ToArrayAsync(ct);

            var sharedByAccount = new Dictionary<uint, HashSet<string>>();
            foreach (var hit in loginHits)
            {
                if (!sharedByAccount.TryGetValue(hit.AccountID, out var set))
                    sharedByAccount[hit.AccountID] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(hit.IPAddress);
            }
            foreach (var hit in directMatches)
            {
                if (string.IsNullOrWhiteSpace(hit.IPAddress)) continue;
                if (!sharedByAccount.TryGetValue(hit.AccountID, out var set))
                    sharedByAccount[hit.AccountID] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(hit.IPAddress);
            }

            if (sharedByAccount.Count > 0)
            {
                var otherIds = sharedByAccount.Keys.Take(50).ToArray();
                var others = await accountDb.Accounts.AsNoTracking()
                    .Where(a => otherIds.Contains(a.AccountID))
                    .OrderByDescending(a => a.AccountID)
                    .ToArrayAsync(ct);
                sameIp = others.Select(o => new SameIpAccountSummary(
                    o.AccountID,
                    o.Username,
                    o.AuthorityID,
                    o.StatusID,
                    DateTime.SpecifyKind(o.Registered, DateTimeKind.Utc),
                    sharedByAccount[o.AccountID].Order(StringComparer.Ordinal).ToArray())).ToArray();
            }
        }

        var onlineCharIds = new HashSet<uint>();
        bool gameConnected = rpc.Connected;
        if (gameConnected)
        {
            try
            {
                var online = await rpc.CallAsync<List<OnlinePlayer>>("OnlinePlayers");
                foreach (var p in online)
                    onlineCharIds.Add(p.CharacterID);
            }
            catch (GameOfflineException)
            {
                gameConnected = false;
            }
        }

        await using var gameDb = new GameDbContext();
        var rawChars = await gameDb.Characters.AsNoTracking()
            .Where(c => c.AccountID == id)
            .OrderByDescending(c => c.Level)
            .ThenBy(c => c.Name)
            .ToArrayAsync(ct);

        var characters = rawChars.Select(c =>
        {
            catalogs.MapsById.TryGetValue(c.MapID, out var map);
            return new AccountCharacterSummary(
                c.CharacterID,
                c.Name,
                c.Level,
                c.Rebirths,
                c.CurrentClass,
                c.MapID,
                map?.Name ?? map?.ServerName,
                c.Silver,
                c.Jewels,
                onlineCharIds.Contains(c.CharacterID));
        }).ToArray();

        return TypedResults.Ok(new AccountDetailResponse(
            account.AccountID,
            account.Username,
            account.Email,
            account.Name,
            account.AuthorityID,
            account.StatusID,
            account.IPAddress,
            DateTime.SpecifyKind(account.Registered, DateTimeKind.Utc),
            gameConnected,
            characters,
            logins,
            sameIp));
    }

    private static async Task<Results<Ok<AccountActionResponse>, JsonHttpResult<ApiError>>> BanAsync(
        uint id,
        AccountBanRequest request,
        ClaimsPrincipal user,
        GameRpc rpc,
        AuditLog audit)
    {
        if (id == user.AccountId())
            return ApiError.Result("self_ban", StatusCodes.Status400BadRequest);
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;

        string? reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var result = await rpc.CallAsync<AdminResult>("Ban", new BanArgs
        {
            ActorAccountID = user.AccountId(),
            AccountID = id,
            Reason = reason,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("account.ban", "account", id.ToString(CultureInfo.InvariantCulture),
            after: new { statusId = (ushort)AccountStatus.Banned, reason },
            summary: reason != null ? $"Banned account #{id}: {reason}" : $"Banned account #{id}");
        return TypedResults.Ok(new AccountActionResponse(true, result.Online));
    }

    private static async Task<Results<Ok<AccountActionResponse>, JsonHttpResult<ApiError>>> UnbanAsync(
        uint id,
        AccountBanRequest request,
        ClaimsPrincipal user,
        GameRpc rpc,
        AuditLog audit)
    {
        if (await OutrankedAsync(id, user) is { } outranked)
            return outranked;
        string? reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var result = await rpc.CallAsync<AdminResult>("Unban", new BanArgs
        {
            ActorAccountID = user.AccountId(),
            AccountID = id,
            Reason = reason,
        });
        if (!result.Ok)
            return ApiError.FromAdmin(result.Error);

        await audit.WriteAsync("account.unban", "account", id.ToString(CultureInfo.InvariantCulture),
            after: new { statusId = (ushort)AccountStatus.Activated, reason },
            summary: reason != null ? $"Unbanned account #{id}: {reason}" : $"Unbanned account #{id}");
        return TypedResults.Ok(new AccountActionResponse(true, result.Online));
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> SetStatusAsync(
        uint id,
        SetAccountStatusRequest request,
        ClaimsPrincipal user,
        AuditLog audit,
        CancellationToken ct)
    {
        if (request.StatusId == (ushort)AccountStatus.Banned)
            return ApiError.Result("use_ban_endpoint", StatusCodes.Status400BadRequest);
        if (!Enum.IsDefined(typeof(AccountStatus), request.StatusId))
            return ApiError.Result("invalid_status", StatusCodes.Status400BadRequest);
        if (id == user.AccountId() && request.StatusId == (ushort)AccountStatus.Locked)
            return ApiError.Result("self_lock", StatusCodes.Status400BadRequest);

        await using var db = new AccountDbContext();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountID == id, ct);
        if (account == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        ushort previous = account.StatusID;
        account.StatusID = request.StatusId;
        await db.SaveChangesAsync(ct);

        string? reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        await audit.WriteAsync("account.set_status", "account", id.ToString(CultureInfo.InvariantCulture),
            before: new { statusId = previous },
            after: new { statusId = request.StatusId, reason },
            summary: $"Status {(AccountStatus)previous} -> {(AccountStatus)request.StatusId}");
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> SetAuthorityAsync(
        uint id,
        SetAccountAuthorityRequest request,
        ClaimsPrincipal user,
        AuditLog audit,
        CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(Authority), request.AuthorityId))
            return ApiError.Result("invalid_authority", StatusCodes.Status400BadRequest);
        if (id == user.AccountId() && request.AuthorityId < (ushort)Authority.Administrator)
            return ApiError.Result("self_demotion", StatusCodes.Status400BadRequest);

        await using var db = new AccountDbContext();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountID == id, ct);
        if (account == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        ushort previous = account.AuthorityID;
        account.AuthorityID = request.AuthorityId;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("account.set_authority", "account", id.ToString(CultureInfo.InvariantCulture),
            before: new { authorityId = previous },
            after: new { authorityId = request.AuthorityId },
            summary: $"Authority {(Authority)previous} -> {(Authority)request.AuthorityId}");
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> ResetPasswordAsync(
        uint id,
        ResetAccountPasswordRequest request,
        AuditLog audit,
        CancellationToken ct)
    {
        if (AccountRules.CheckPassword(request.NewPassword) is string validationError)
            return ApiError.Result(validationError, StatusCodes.Status400BadRequest);

        await using var db = new AccountDbContext();
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.AccountID == id, ct);
        if (account == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);

        string? error = await AccountsRepository.SetPasswordAsync(account.Username, request.NewPassword);
        if (error != null)
            return ApiError.Result(error, StatusCodes.Status400BadRequest);

        await audit.WriteAsync("account.reset_password", "account", id.ToString(CultureInfo.InvariantCulture),
            summary: $"Password reset for {account.Username}");
        return TypedResults.NoContent();
    }

    // 404 for no such account, 403 when it is the user's rank or above, else null
    internal static async Task<JsonHttpResult<ApiError>?> OutrankedAsync(uint accountId, ClaimsPrincipal user)
    {
        await using var db = new AccountDbContext();
        var authority = await db.Accounts.AsNoTracking().Where(a => a.AccountID == accountId)
            .Select(a => (ushort?)a.AuthorityID).SingleOrDefaultAsync();
        if (authority == null)
            return ApiError.Result("not_found", StatusCodes.Status404NotFound);
        return user.Outranks(authority.Value) ? null : ApiError.Result("outranked", StatusCodes.Status403Forbidden);
    }
}

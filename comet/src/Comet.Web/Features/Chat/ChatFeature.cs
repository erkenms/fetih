namespace Comet.Web.Features.Chat;

using System.Globalization;
using System.Security.Claims;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Features.Audit;
using Comet.Web.Features.Dashboard;
using Comet.Web.Features.Logs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using AccountDbContext = Comet.Account.Database.ServerDbContext;

public sealed class ChatFeature : IFeature
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;
    private const int MaxResolvedAccounts = 50;
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(30);

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/chat").RequirePermission(Permission.ViewLogs);
        group.MapGet("/messages", QueryMessagesAsync)
            .WithName("queryChatMessages")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/gm-commands", QueryGmCommandsAsync)
            .WithName("queryGmCommands")
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ChatMessagesResponse>, JsonHttpResult<ApiError>>> QueryMessagesAsync(
        ILokiClient loki,
        ObservabilityOptions options,
        string? channel = null,
        string? character = null,
        string? account = null,
        string? words = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        if (!TryNormalizeRange(from, to, out var startUtc, out var endUtc))
            return ApiError.Result("invalid_time_range", StatusCodes.Status400BadRequest);

        int clampedLimit = Math.Clamp(limit, 1, MaxLimit);
        var (characterId, characterName) = ParseCharacterFilter(character);
        var (accountIds, noAccountMatch) = await ResolveAccountFilterAsync(account, ct);

        var filter = new ChatQueryFilter(channel, characterId, characterName, accountIds, words);
        string query = ChatLogQl.BuildChatQuery(filter);
        string exploreUrl = LogQl.BuildGrafanaExploreUrl(options.GrafanaUrl, query, startUtc, endUtc);

        if (noAccountMatch)
            return TypedResults.Ok(new ChatMessagesResponse([], query, exploreUrl, startUtc, endUtc, false));

        try
        {
            var raw = await loki.QueryRangeAsync(query, startUtc, endUtc, clampedLimit, ct);
            var entries = raw.Select(ChatLogQl.ParseEntry).ToArray();
            return TypedResults.Ok(new ChatMessagesResponse(
                entries, query, exploreUrl, startUtc, endUtc, raw.Length >= clampedLimit));
        }
        catch (LokiUnavailableException)
        {
            return ApiError.Result("loki_unavailable", StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<Results<Ok<GmCommandsResponse>, JsonHttpResult<ApiError>>> QueryGmCommandsAsync(
        ClaimsPrincipal user,
        ILokiClient loki,
        IDbContextFactory<WebDbContext> auditDbFactory,
        ObservabilityOptions options,
        string? character = null,
        string? account = null,
        string? command = null,
        string? words = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        if (!TryNormalizeRange(from, to, out var startUtc, out var endUtc))
            return ApiError.Result("invalid_time_range", StatusCodes.Status400BadRequest);

        int clampedLimit = Math.Clamp(limit, 1, MaxLimit);
        var (characterId, characterName) = ParseCharacterFilter(character);
        var (accountIds, noAccountMatch) = await ResolveAccountFilterAsync(account, ct);

        var filter = new GmCommandQueryFilter(characterId, characterName, accountIds, command, words);
        string query = ChatLogQl.BuildGmCommandQuery(filter);
        string exploreUrl = LogQl.BuildGrafanaExploreUrl(options.GrafanaUrl, query, startUtc, endUtc);

        ChatLogEntry[] commands = [];
        bool lokiAvailable = true;
        bool truncated = false;

        if (!noAccountMatch)
        {
            try
            {
                var raw = await loki.QueryRangeAsync(query, startUtc, endUtc, clampedLimit, ct);
                commands = raw.Select(ChatLogQl.ParseEntry).ToArray();
                truncated = raw.Length >= clampedLimit;
            }
            catch (LokiUnavailableException)
            {
                lokiAvailable = false;
            }
        }

        bool canViewAudit = user.Has(Permission.ViewAudit);
        AuditEntry[] auditEntries = [];
        if (canViewAudit && !noAccountMatch)
        {
            await using var db = await auditDbFactory.CreateDbContextAsync(ct);
            IQueryable<DbWebAudit> auditQuery = db.Audit.AsNoTracking()
                .Where(x => x.Timestamp >= startUtc && x.Timestamp <= endUtc);

            if (accountIds is { Length: > 0 })
                auditQuery = auditQuery.Where(x => accountIds.Contains(x.AccountID));

            string? term = string.IsNullOrWhiteSpace(words) ? null : words.Trim();
            if (term != null)
            {
                auditQuery = auditQuery.Where(x =>
                    x.Action.Contains(term) ||
                    x.Username.Contains(term) ||
                    (x.Summary != null && x.Summary.Contains(term)) ||
                    (x.TargetID != null && x.TargetID.Contains(term)));
            }

            auditEntries = await auditQuery
                .OrderByDescending(x => x.ID)
                .Take(clampedLimit)
                .Select(x => new AuditEntry(
                    x.ID,
                    DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc),
                    x.AccountID,
                    x.Username,
                    x.IPAddress,
                    x.Action,
                    x.TargetType,
                    x.TargetID,
                    x.Summary,
                    x.Before,
                    x.After))
                .ToArrayAsync(ct);
        }

        return TypedResults.Ok(new GmCommandsResponse(
            commands, auditEntries, canViewAudit, lokiAvailable, query, exploreUrl, startUtc, endUtc, truncated));
    }

    private static bool TryNormalizeRange(DateTime? from, DateTime? to, out DateTime startUtc, out DateTime endUtc)
    {
        endUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
        startUtc = from?.ToUniversalTime() ?? endUtc.AddHours(-24);
        return startUtc < endUtc && endUtc - startUtc <= MaxRange;
    }

    private static (uint? Id, string? Name) ParseCharacterFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);
        string trimmed = raw.Trim();
        if (uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) && id > 0)
            return (id, null);
        return (null, trimmed);
    }

    private static async Task<(uint[]? Ids, bool NoMatch)> ResolveAccountFilterAsync(string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, false);
        string trimmed = raw.Trim();
        if (uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) && id > 0)
            return ([id], false);

        await using var accountDb = new AccountDbContext();
        uint[] matches = await accountDb.Accounts.AsNoTracking()
            .Where(a => a.Username.Contains(trimmed) || (a.Email != null && a.Email.Contains(trimmed)))
            .Select(a => a.AccountID)
            .Take(MaxResolvedAccounts)
            .ToArrayAsync(ct);

        return matches.Length == 0 ? (null, true) : (matches, false);
    }
}

namespace Comet.Web.Features.Players;

using System.Globalization;
using System.Security.Claims;
using Comet.Shared.Models.Admin;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Features.Accounts;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.Characters;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccountDbContext = Comet.Account.Database.ServerDbContext;
using GameDbContext = Comet.Game.Database.ServerDbContext;

// Accounts and their characters on one page. Rows are joined in memory: the account and game
// databases are separate contexts, and online players' level, class and map come from the game
// server, not the database.
public sealed class PlayersFeature : IFeature
{
    private const int MaxBulk = 2000;

    public void AddServices(IServiceCollection services, WebConfiguration config) { }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/players").RequirePermission(Permission.AdminArea);
        group.MapGet("/", ListAsync).WithName("listPlayers");
        group.MapGet("/ids", IdsAsync).WithName("listPlayerIds");
        group.MapPost("/bulk", BulkAsync)
            .RequirePermission(Permission.Moderate)
            .WithName("bulkPlayerAction")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status403Forbidden);
    }

    public record Filter(
        [FromQuery(Name = "q")] string? Q = null,
        [FromQuery(Name = "ip")] string? Ip = null,
        [FromQuery(Name = "status")] ushort? Status = null,
        [FromQuery(Name = "authority")] ushort? Authority = null,
        [FromQuery(Name = "online")] bool Online = false,
        [FromQuery(Name = "classLine")] byte? ClassLine = null,
        [FromQuery(Name = "mapId")] uint? MapId = null,
        [FromQuery(Name = "minLevel")] byte? MinLevel = null,
        [FromQuery(Name = "maxLevel")] byte? MaxLevel = null,
        [FromQuery(Name = "sort")] string? Sort = null);

    private static async Task<Ok<PlayerListResponse>> ListAsync(
        GameRpc rpc,
        CatalogStore catalogs,
        [AsParameters] Filter filter,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var (rows, gameConnected) = await QueryAsync(rpc, catalogs, filter, ct);
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        var ids = items.Select(r => r.AccountId).ToArray();
        if (ids.Length > 0)
        {
            await using var accountDb = new AccountDbContext();
            var lastLogins = await accountDb.Logins.AsNoTracking()
                .Where(l => ids.Contains(l.AccountID))
                .GroupBy(l => l.AccountID)
                .Select(g => new { AccountId = g.Key, Last = g.Max(x => x.Timestamp) })
                .ToDictionaryAsync(x => x.AccountId, x => DateTime.SpecifyKind(x.Last, DateTimeKind.Utc), ct);
            items = items
                .Select(r => lastLogins.TryGetValue(r.AccountId, out var last) ? r with { LastLogin = last } : r)
                .ToArray();
        }

        return TypedResults.Ok(new PlayerListResponse(items, rows.Count, page, pageSize, gameConnected));
    }

    private static async Task<Ok<PlayerIdsResponse>> IdsAsync(
        GameRpc rpc,
        CatalogStore catalogs,
        [AsParameters] Filter filter,
        CancellationToken ct = default)
    {
        var (rows, _) = await QueryAsync(rpc, catalogs, filter, ct);
        return TypedResults.Ok(new PlayerIdsResponse(rows.Select(r => r.AccountId).Distinct().ToArray()));
    }

    private static async Task<(List<PlayerListItem> Rows, bool GameConnected)> QueryAsync(
        GameRpc rpc, CatalogStore catalogs, Filter f, CancellationToken ct)
    {
        var onlineById = new Dictionary<uint, OnlinePlayer>();
        bool gameConnected = rpc.Connected;
        if (gameConnected)
        {
            try
            {
                foreach (var p in await rpc.CallAsync<List<OnlinePlayer>>("OnlinePlayers"))
                    onlineById[p.CharacterID] = p;
            }
            catch (GameOfflineException)
            {
                gameConnected = false;
            }
        }

        if (f.Online && onlineById.Count == 0)
            return ([], gameConnected);

        await using var gameDb = new GameDbContext();
        var charQuery = gameDb.Characters.AsNoTracking();
        if (f.Online)
        {
            var onlineIds = onlineById.Keys.ToArray();
            charQuery = charQuery.Where(c => onlineIds.Contains(c.CharacterID));
        }
        var characters = (await charQuery
            .Select(c => new
            {
                c.CharacterID, c.AccountID, c.Name, c.Level, c.Rebirths, c.CurrentClass,
                c.MapID, c.X, c.Y, c.Silver, c.Jewels, c.KillPoints,
            })
            .ToArrayAsync(ct))
            .Select(c =>
            {
                onlineById.TryGetValue(c.CharacterID, out var live);
                return new
                {
                    c.CharacterID, c.AccountID, c.Name,
                    Level = live?.Level ?? c.Level,
                    Rebirths = live?.Rebirths ?? c.Rebirths,
                    Class = live?.Class ?? c.CurrentClass,
                    MapID = live?.MapID ?? c.MapID,
                    X = live?.X ?? c.X,
                    Y = live?.Y ?? c.Y,
                    c.Silver, c.Jewels,
                    KillPoints = live?.PkPoints ?? c.KillPoints,
                    Online = live != null,
                };
            })
            .Where(c => f.MinLevel == null || c.Level >= f.MinLevel)
            .Where(c => f.MaxLevel == null || c.Level <= f.MaxLevel)
            .Where(c => f.MapId == null || c.MapID == f.MapId)
            .Where(c => f.ClassLine == null || ClassLine(c.Class) == f.ClassLine)
            .ToLookup(c => c.AccountID);

        bool needsCharacter = f.Online || f.ClassLine != null || f.MapId != null
            || f.MinLevel != null || f.MaxLevel != null;

        await using var accountDb = new AccountDbContext();
        var accountQuery = accountDb.Accounts.AsNoTracking();
        if (f.Status.HasValue)
            accountQuery = accountQuery.Where(a => a.StatusID == f.Status.Value);
        if (f.Authority.HasValue)
            accountQuery = accountQuery.Where(a => a.AuthorityID == f.Authority.Value);
        string? ip = string.IsNullOrWhiteSpace(f.Ip) ? null : f.Ip.Trim();
        if (ip != null)
        {
            var loginAccountIds = accountDb.Logins.AsNoTracking()
                .Where(l => l.IPAddress == ip)
                .Select(l => l.AccountID);
            accountQuery = accountQuery.Where(a => a.IPAddress == ip || loginAccountIds.Contains(a.AccountID));
        }
        if (needsCharacter)
        {
            var accountIds = characters.Select(g => g.Key).ToArray();
            accountQuery = accountQuery.Where(a => accountIds.Contains(a.AccountID));
        }
        var accounts = await accountQuery
            .Select(a => new { a.AccountID, a.Username, a.Email, a.Name, a.AuthorityID, a.StatusID, a.IPAddress, a.Registered })
            .ToArrayAsync(ct);

        string? term = string.IsNullOrWhiteSpace(f.Q) ? null : f.Q.Trim();
        bool isId = uint.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsedId);
        bool Has(string? s) => s != null && s.Contains(term!, StringComparison.OrdinalIgnoreCase);

        var rows = new List<PlayerListItem>();
        foreach (var a in accounts)
        {
            var chars = characters[a.AccountID].ToArray();
            if (chars.Length == 0 && needsCharacter)
                continue;
            bool accountMatch = term == null || (isId && a.AccountID == parsedId)
                || Has(a.Username) || Has(a.Email) || Has(a.Name) || Has(a.IPAddress);
            var registered = DateTime.SpecifyKind(a.Registered, DateTimeKind.Utc);

            if (chars.Length == 0)
            {
                if (accountMatch)
                    rows.Add(new PlayerListItem(a.AccountID, a.Username, a.Email, a.AuthorityID, a.StatusID,
                        a.IPAddress, registered, null, null, null, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, false));
                continue;
            }
            foreach (var c in chars)
            {
                if (!accountMatch && !(isId && c.CharacterID == parsedId) && !Has(c.Name))
                    continue;
                rows.Add(new PlayerListItem(a.AccountID, a.Username, a.Email, a.AuthorityID, a.StatusID,
                    a.IPAddress, registered, null, c.CharacterID, c.Name, c.Level, c.Rebirths, c.Class,
                    c.MapID, catalogs.MapDisplayName(c.MapID), c.X, c.Y, c.Silver, c.Jewels,
                    c.KillPoints, c.Online));
            }
        }

        IEnumerable<PlayerListItem> sorted = (f.Sort ?? (f.Online ? "level" : "newest")) switch
        {
            "level" => rows.OrderByDescending(r => r.Rebirths).ThenByDescending(r => r.Level)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "name" => rows.OrderBy(r => r.Name ?? r.Username, StringComparer.OrdinalIgnoreCase),
            "silver" => rows.OrderByDescending(r => r.Silver),
            "cp" => rows.OrderByDescending(r => r.ConquerPoints),
            "pk" => rows.OrderByDescending(r => r.KillPoints),
            _ => rows.OrderByDescending(r => r.AccountId),
        };
        return (sorted.ToList(), gameConnected);
    }

    // Taoists are 100-101 and 132-145; every other line is its class / 10
    public static byte ClassLine(byte cls) => cls >= 100 ? (byte)10 : (byte)(cls / 10);

    private static async Task<Results<Ok<PlayerBulkResponse>, JsonHttpResult<ApiError>>> BulkAsync(
        PlayerBulkRequest request,
        ClaimsPrincipal user,
        GameRpc rpc,
        AuditLog audit,
        CancellationToken ct)
    {
        Permission? needed = request.Action switch
        {
            "kick" or "mute" or "jail" or "message" => Permission.Moderate,
            "giveItem" or "silver" or "cp" or "level" or "teleport" or "ban" or "unban" => Permission.ManageCharacters,
            _ => null,
        };
        if (needed == null)
            return ApiError.Result("invalid_action", StatusCodes.Status400BadRequest);
        if (!user.Has(needed.Value))
            return ApiError.Result("forbidden", StatusCodes.Status403Forbidden);
        var accountIds = (request.AccountIds ?? []).Distinct().ToArray();
        if (accountIds.Length == 0 || accountIds.Length > MaxBulk)
            return ApiError.Result("invalid_selection", StatusCodes.Status400BadRequest);
        if (request.Action == "giveItem" && request.Item == null)
            return ApiError.Result("invalid", StatusCodes.Status400BadRequest);

        await using var gameDb = new GameDbContext();
        var characterByAccount = (await gameDb.Characters.AsNoTracking()
                .Where(c => accountIds.Contains(c.AccountID))
                .Select(c => new { c.AccountID, c.CharacterID, c.Name })
                .ToArrayAsync(ct))
            .GroupBy(c => c.AccountID)
            .ToDictionary(g => g.Key, g => g.First());

        int succeeded = 0;
        var failures = new List<PlayerBulkFailure>();
        foreach (uint accountId in accountIds)
        {
            characterByAccount.TryGetValue(accountId, out var ch);
            string? error;
            try
            {
                error = await RunAsync(request, accountId, ch?.CharacterID, user, rpc, audit, ct);
            }
            catch (GameOfflineException)
            {
                error = "game_offline";
            }
            if (error == null)
                succeeded++;
            else
                failures.Add(new PlayerBulkFailure(accountId, ch?.Name, error));
        }
        return TypedResults.Ok(new PlayerBulkResponse(succeeded, failures.ToArray()));
    }

    private static async Task<string?> RunAsync(
        PlayerBulkRequest r, uint accountId, uint? characterId,
        ClaimsPrincipal user, GameRpc rpc, AuditLog audit, CancellationToken ct)
    {
        if (r.Action is "ban" or "unban")
        {
            var ban = new AccountBanRequest(r.Reason);
            return Error(r.Action == "ban"
                ? await AccountsFeature.BanAsync(accountId, ban, user, rpc, audit)
                : await AccountsFeature.UnbanAsync(accountId, ban, user, rpc, audit));
        }

        if (characterId is not { } id)
            return "no_character";
        return Error(r.Action switch
        {
            "kick" => await CharactersFeature.KickAsync(id, new KickCharacterRequest(r.Reason), user, rpc, audit),
            "mute" => await CharactersFeature.MuteAsync(id, new MuteCharacterRequest(r.Minutes), user, rpc, audit),
            "jail" => await CharactersFeature.JailAsync(id, user, rpc, audit),
            "message" => await CharactersFeature.MessageAsync(id, new MessageCharacterRequest(r.Text ?? ""), user, rpc, audit),
            "giveItem" => await CharactersFeature.GiveItemAsync(id, r.Item!, user, rpc, audit),
            "silver" => await CharactersFeature.AdjustSilverAsync(id, new AdjustAmountRequest(r.Amount), user, rpc, audit),
            "cp" => await CharactersFeature.AdjustConquerPointsAsync(id, new AdjustAmountRequest(r.Amount), user, rpc, audit),
            "level" => await CharactersFeature.SetLevelAsync(id, new SetLevelRequest(r.Level), user, rpc, audit),
            "teleport" => await CharactersFeature.TeleportAsync(id, new TeleportCharacterRequest(r.MapId, r.X, r.Y), user, rpc, audit),
            _ => throw new InvalidOperationException(r.Action),
        });
    }

    private static string? Error(IResult result) => result is INestedHttpResult { Result: JsonHttpResult<ApiError> e }
        ? e.Value?.Error ?? "failed"
        : null;
}

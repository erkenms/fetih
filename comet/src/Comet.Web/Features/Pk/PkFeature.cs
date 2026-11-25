namespace Comet.Web.Features.Pk;

using System.Globalization;
using Comet.Core.Logging;
using Comet.Game.Database;
using Comet.Game.Database.Models;
using Comet.Game.Pk;
using Comet.Shared.Models.Admin;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.Items;
using Comet.Web.Features.Logs;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

// PK jail inmates (map 6000) with release, live flashing/red/black players, PK points
// leaderboard and detained items awaiting redemption or hunter claim.
public class PkFeature : IFeature
{
    public const int ListLimit = 200;
    private static readonly TimeSpan JailLogLookback = TimeSpan.FromDays(30);
    private static readonly TimeSpan JailLogTimeout = TimeSpan.FromSeconds(2);

    public void Map(RouteGroupBuilder api)
    {
        var pk = api.MapGroup("/pk").RequirePermission(Permission.AdminArea);
        pk.MapGet("/summary", SummaryAsync).WithName("pkSummary");
        pk.MapGet("/jail", JailAsync).WithName("pkJail");
        pk.MapPost("/jail/{id}/release", ReleaseAsync)
            .WithName("pkRelease")
            .RequirePermission(Permission.Moderate)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status409Conflict);
        pk.MapGet("/active", ActiveAsync).WithName("pkActive");
        pk.MapGet("/leaderboard", LeaderboardAsync).WithName("pkLeaderboard");
        pk.MapGet("/detained", DetainedAsync).WithName("pkDetained");
    }

    private static async Task<PkSummaryResponse> SummaryAsync(GameRpc game, CancellationToken ct)
    {
        var (connected, snapshot, online) = await FetchLiveAsync(game);

        await using var db = new ServerDbContext();
        var jailedDbIds = await db.Characters.AsNoTracking()
            .Where(c => c.MapID == Jail.MapID)
            .Select(c => c.CharacterID)
            .ToListAsync(ct);

        var onlineById = online.ToDictionary(p => p.CharacterID);
        var jailedIds = new HashSet<uint>();
        foreach (uint id in jailedDbIds)
        {
            if (onlineById.TryGetValue(id, out var live) && live.MapID != Jail.MapID)
                continue;
            jailedIds.Add(id);
        }
        int jailedOnline = 0;
        if (snapshot != null)
        {
            jailedOnline = snapshot.Jailed.Count;
            foreach (var p in snapshot.Jailed)
                jailedIds.Add(p.CharacterID);
        }

        int activeTotal = snapshot?.Active.Count ?? 0;
        int flashingCount = snapshot?.Active.Count(p => p.Flashing) ?? 0;
        int redCount = snapshot?.Active.Count(p => p.PkPoints >= Pking.RedNamePoints && p.PkPoints < Pking.BlackNamePoints) ?? 0;
        int blackCount = snapshot?.Active.Count(p => p.PkPoints >= Pking.BlackNamePoints) ?? 0;

        var now = DateTime.UtcNow;
        var detained = await db.DetainedItems.AsNoTracking()
            .Select(d => new { d.Redeemed, d.DetainedAt })
            .ToListAsync(ct);
        int redeemedCount = 0, expiredCount = 0, redeemableCount = 0;
        foreach (var d in detained)
        {
            if (d.Redeemed)
                redeemedCount++;
            else if (now >= Utc(d.DetainedAt) + Detaining.DetainPeriod)
                expiredCount++;
            else
                redeemableCount++;
        }

        return new PkSummaryResponse(
            connected,
            jailedIds.Count,
            jailedOnline,
            activeTotal,
            flashingCount,
            redCount,
            blackCount,
            redeemableCount,
            expiredCount,
            redeemedCount,
            snapshot?.TakenAt is DateTime takenAt ? Utc(takenAt) : null);
    }

    private static async Task<PkJailResponse> JailAsync(
        GameRpc game, ILokiClient loki, string? search, CancellationToken ct)
    {
        var (connected, snapshot, online) = await FetchLiveAsync(game);
        var onlineById = online.ToDictionary(p => p.CharacterID);

        await using var db = new ServerDbContext();
        var saved = await db.Characters.AsNoTracking()
            .Where(c => c.MapID == Jail.MapID)
            .ToListAsync(ct);

        var rows = new List<(PkCharacterRef Character, ushort PkPoints, string Color, bool Flashing, bool Online, ushort X, ushort Y)>();
        var seen = new HashSet<uint>();

        foreach (var live in snapshot?.Jailed ?? [])
        {
            seen.Add(live.CharacterID);
            rows.Add((
                new PkCharacterRef(live.CharacterID, live.Name, live.Level, live.Class),
                live.PkPoints,
                live.NameColor,
                live.Flashing,
                true,
                live.X,
                live.Y));
        }

        foreach (var c in saved)
        {
            if (seen.Contains(c.CharacterID))
                continue;
            // Online outside the jail: the character left it since the last save
            if (onlineById.ContainsKey(c.CharacterID))
                continue;

            rows.Add((
                new PkCharacterRef(c.CharacterID, c.Name, c.Level, c.CurrentClass),
                c.KillPoints,
                ColorForPoints(c.KillPoints),
                false,
                false,
                c.X,
                c.Y));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            rows = rows.Where(r =>
                r.Character.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Character.Id.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal))
                .ToList();
        }

        var jailEvents = await FetchJailTimesAsync(loki, rows.Select(r => r.Character.Id).ToHashSet(), ct);

        var inmates = rows
            .OrderByDescending(r => r.Online)
            .ThenByDescending(r => r.PkPoints)
            .ThenBy(r => r.Character.Name, StringComparer.OrdinalIgnoreCase)
            .Take(ListLimit)
            .Select(r =>
            {
                bool hasInfo = jailEvents.TryGetValue(r.Character.Id, out var info);
                return new JailedCharacterView(
                    r.Character,
                    r.PkPoints,
                    r.Color,
                    r.Flashing,
                    r.Online,
                    r.X,
                    r.Y,
                    hasInfo ? info.Since : null,
                    hasInfo ? info.Reason : null,
                    r.PkPoints >= Pking.BlackNamePoints ? Jail.Bail : 0u);
            })
            .ToArray();

        return new PkJailResponse(connected, inmates);
    }

    private static async Task<Results<Ok<AdminResult>, NotFound<ApiError>, Conflict<ApiError>>> ReleaseAsync(
        uint id, GameRpc game, AuditLog audit, HttpContext http, CancellationToken ct)
    {
        DbCharacter? before;
        await using (var db = new ServerDbContext())
            before = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.CharacterID == id, ct);
        if (before == null)
            return TypedResults.NotFound(new ApiError("not_found"));

        var result = await game.CallAsync<AdminResult>("ReleaseFromJail",
            new CharacterArgs { ActorAccountID = http.User.AccountId(), CharacterID = id });
        if (!result.Ok)
        {
            return result.Error switch
            {
                AdminErrors.NotFound => TypedResults.NotFound(new ApiError("not_found")),
                AdminErrors.Invalid => TypedResults.Conflict(new ApiError("not_in_jail")),
                _ => TypedResults.Conflict(new ApiError(result.Error ?? "conflict")),
            };
        }

        await audit.WriteAsync(
            "pk.release",
            "character",
            id.ToString(CultureInfo.InvariantCulture),
            before: new { before.Name, before.MapID, before.X, before.Y, before.KillPoints },
            summary: $"Released {before.Name} from jail");
        return TypedResults.Ok(result);
    }

    private static async Task<ActivePkResponse> ActiveAsync(
        GameRpc game, CatalogStore catalogs, string? color, string? search)
    {
        var (connected, snapshot) = await FetchSnapshotAsync(game);
        if (!connected || snapshot == null)
            return new ActivePkResponse(false, null, []);

        IEnumerable<ActivePkPlayer> players = snapshot.Active;
        if (!string.IsNullOrWhiteSpace(color))
        {
            string filter = color.Trim().ToLowerInvariant();
            players = filter switch
            {
                "blue" or "flashing" => players.Where(p => p.Flashing),
                "red" => players.Where(p => p.PkPoints >= Pking.RedNamePoints && p.PkPoints < Pking.BlackNamePoints),
                "black" => players.Where(p => p.PkPoints >= Pking.BlackNamePoints),
                _ => players,
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            players = players.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.CharacterID.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal));
        }

        var views = players.Select(p => new ActivePkView(
            new PkCharacterRef(p.CharacterID, p.Name, p.Level, p.Class),
            p.Rebirths,
            p.MapID,
            MapName(catalogs, p.MapID),
            p.X,
            p.Y,
            p.PkPoints,
            p.Flashing,
            p.NameColor,
            p.PkMode,
            p.InJail,
            p.Dead)).ToArray();

        return new ActivePkResponse(true, Utc(snapshot.TakenAt), views);
    }

    private static async Task<PkLeaderboardResponse> LeaderboardAsync(
        GameRpc game, CatalogStore catalogs, string? color, string? search, CancellationToken ct)
    {
        var (connected, snapshot, online) = await FetchLiveAsync(game);
        var onlineById = online.ToDictionary(p => p.CharacterID);
        var activeById = (snapshot?.Active ?? []).ToDictionary(p => p.CharacterID);

        await using var db = new ServerDbContext();
        var query = db.Characters.AsNoTracking().Where(c => c.KillPoints > 0);

        if (!string.IsNullOrWhiteSpace(color))
        {
            string filter = color.Trim().ToLowerInvariant();
            query = filter switch
            {
                "black" => query.Where(c => c.KillPoints >= Pking.BlackNamePoints),
                "red" => query.Where(c => c.KillPoints >= Pking.RedNamePoints && c.KillPoints < Pking.BlackNamePoints),
                _ => query,
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(c => c.Name.Contains(term));
        }

        var saved = await query
            .OrderByDescending(c => c.KillPoints)
            .ThenByDescending(c => c.Level)
            .ThenBy(c => c.CharacterID)
            .Take(ListLimit)
            .ToListAsync(ct);

        var rows = new List<(PkCharacterRef Character, byte Rebirths, ushort PkPoints, string Color, bool Flashing, bool Online, uint MapId)>();
        var seen = new HashSet<uint>();

        foreach (var c in saved)
        {
            seen.Add(c.CharacterID);
            bool isOnline = onlineById.TryGetValue(c.CharacterID, out var live);
            activeById.TryGetValue(c.CharacterID, out var active);
            ushort points = active?.PkPoints ?? live?.PkPoints ?? c.KillPoints;
            uint mapId = active?.MapID ?? live?.MapID ?? c.MapID;
            bool flashing = active?.Flashing ?? false;
            string nameColor = active?.NameColor ?? ColorForPoints(points);

            rows.Add((
                new PkCharacterRef(c.CharacterID, c.Name, live?.Level ?? c.Level, live?.Class ?? c.CurrentClass),
                live?.Rebirths ?? c.Rebirths,
                points,
                nameColor,
                flashing,
                isOnline,
                mapId));
        }

        // Include any online player whose live PK points climbed since the last DB save
        foreach (var live in online)
        {
            if (seen.Contains(live.CharacterID) || live.PkPoints == 0)
                continue;
            if (!MatchesLeaderboardFilter(live.PkPoints, live.Name, color, search))
                continue;
            activeById.TryGetValue(live.CharacterID, out var active);
            rows.Add((
                new PkCharacterRef(live.CharacterID, live.Name, live.Level, live.Class),
                live.Rebirths,
                live.PkPoints,
                active?.NameColor ?? ColorForPoints(live.PkPoints),
                active?.Flashing ?? false,
                true,
                live.MapID));
        }

        var entries = rows
            .Where(r => r.PkPoints > 0)
            .OrderByDescending(r => r.PkPoints)
            .ThenByDescending(r => r.Character.Level)
            .ThenBy(r => r.Character.Id)
            .Take(ListLimit)
            .Select((r, i) => new PkLeaderboardEntry(
                i + 1,
                r.Character,
                r.Rebirths,
                r.PkPoints,
                r.Color,
                r.Flashing,
                r.Online,
                r.MapId,
                MapName(catalogs, r.MapId),
                r.MapId == Jail.MapID))
            .ToArray();

        return new PkLeaderboardResponse(connected, entries);
    }

    private static async Task<DetainedItemsResponse> DetainedAsync(
        ItemCatalog catalog, string? status, string? search, CancellationToken ct)
    {
        await using var db = new ServerDbContext();
        var entries = await db.DetainedItems.AsNoTracking()
            .OrderByDescending(d => d.DetainedAt)
            .Take(ListLimit)
            .ToListAsync(ct);

        var itemIds = entries.Where(e => e.ItemID != 0).Select(e => e.ItemID).Distinct().ToList();
        var itemsById = itemIds.Count == 0
            ? new Dictionary<uint, DbItem>()
            : await db.Items.AsNoTracking()
                .Where(i => itemIds.Contains(i.ItemID))
                .ToDictionaryAsync(i => i.ItemID, ct);

        var charIds = entries.SelectMany(e => new[] { e.TargetID, e.HunterID })
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        var charsById = charIds.Count == 0
            ? new Dictionary<uint, PkCharacterRef>()
            : await db.Characters.AsNoTracking()
                .Where(c => charIds.Contains(c.CharacterID))
                .Select(c => new PkCharacterRef(c.CharacterID, c.Name, c.Level, c.CurrentClass))
                .ToDictionaryAsync(c => c.Id, ct);

        var now = DateTime.UtcNow;
        int redeemableCount = 0, expiredCount = 0, redeemedCount = 0;
        var views = new List<DetainedItemView>(entries.Count);

        foreach (var e in entries)
        {
            var detainedAt = Utc(e.DetainedAt);
            var expiresAt = detainedAt + Detaining.DetainPeriod;
            uint days = Detaining.DaysElapsed(detainedAt, now);
            string state = e.Redeemed ? "redeemed"
                : now >= expiresAt ? "expired"
                : "redeemable";

            if (state == "redeemed") redeemedCount++;
            else if (state == "expired") expiredCount++;
            else redeemableCount++;

            itemsById.TryGetValue(e.ItemID, out var item);
            CatalogItem? catItem = item != null && catalog.TryGet(item.TypeID, out var found) ? found : null;
            DetainedItemInstance? instance = item == null ? null : new DetainedItemInstance(
                item.ItemID,
                item.TypeID,
                item.Plus,
                item.Bless,
                item.Enchant,
                item.Gem1,
                item.Gem2,
                item.Color,
                item.Amount,
                item.AmountLimit,
                item.Locked);

            var owner = charsById.GetValueOrDefault(e.TargetID)
                ?? new PkCharacterRef(e.TargetID, string.IsNullOrEmpty(e.TargetName) ? $"#{e.TargetID}" : e.TargetName, 0, 0);
            var killer = charsById.GetValueOrDefault(e.HunterID)
                ?? new PkCharacterRef(e.HunterID, string.IsNullOrEmpty(e.HunterName) ? $"#{e.HunterID}" : e.HunterName, 0, 0);

            views.Add(new DetainedItemView(
                e.DetainID,
                e.ItemID,
                instance,
                catItem,
                owner,
                killer,
                e.Cost,
                detainedAt,
                expiresAt,
                days,
                e.Redeemed,
                state));
        }

        IEnumerable<DetainedItemView> filtered = views;
        if (!string.IsNullOrWhiteSpace(status))
        {
            string wanted = status.Trim().ToLowerInvariant();
            if (wanted is "redeemable" or "expired" or "redeemed")
                filtered = filtered.Where(v => v.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            filtered = filtered.Where(v =>
                v.Owner.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                v.Killer.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (v.CatalogItem?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                v.DetainId.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal));
        }

        return new DetainedItemsResponse(filtered.ToArray(), redeemableCount, expiredCount, redeemedCount);
    }

    private static async Task<(bool Connected, PkLiveSnapshot? Snapshot)> FetchSnapshotAsync(GameRpc game)
    {
        if (!game.Connected)
            return (false, null);
        try
        {
            return (true, await game.CallAsync<PkLiveSnapshot>("PkSnapshot"));
        }
        catch (GameOfflineException)
        {
            return (false, null);
        }
    }

    private static async Task<(bool Connected, PkLiveSnapshot? Snapshot, List<OnlinePlayer> Online)> FetchLiveAsync(GameRpc game)
    {
        if (!game.Connected)
            return (false, null, []);
        try
        {
            var snapshot = await game.CallAsync<PkLiveSnapshot>("PkSnapshot");
            var online = await game.CallAsync<List<OnlinePlayer>>("OnlinePlayers");
            return (true, snapshot, online);
        }
        catch (GameOfflineException)
        {
            return (false, null, []);
        }
    }

    // Best-effort lookup of when each inmate was jailed from game logs (pk.jailed / gm.jail).
    private static async Task<Dictionary<uint, (DateTime Since, string? Reason)>> FetchJailTimesAsync(
        ILokiClient loki,
        HashSet<uint> wantedIds,
        CancellationToken ct)
    {
        if (wantedIds.Count == 0)
            return [];

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(JailLogTimeout);
            var end = DateTime.UtcNow;
            var start = end - JailLogLookback;
            var entries = await loki.QueryRangeAsync(
                "{server=\"game\", event=~\"pk\\\\.jailed|gm\\\\.jail\"}",
                start,
                end,
                500,
                timeout.Token);

            return JailTimes(entries, wantedIds);
        }
        catch (LokiUnavailableException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

        return [];
    }

    // The newest jailing of each wanted character, from entries newest first
    internal static Dictionary<uint, (DateTime Since, string? Reason)> JailTimes(IEnumerable<LogEntry> entries, HashSet<uint> wantedIds)
    {
        var result = new Dictionary<uint, (DateTime Since, string? Reason)>();
        foreach (var entry in entries)
        {
            // gm.jail is logged for failed attempts too; its characterId is the target
            if (entry.Event == LogEvents.GmJail
                && !(entry.Fields.TryGetValue("result", out var outcome) && outcome is "online" or "offline"))
                continue;
            if (entry.CharacterId is not uint id || !wantedIds.Contains(id) || result.ContainsKey(id))
                continue;

            string? reason = entry.Fields.TryGetValue("reason", out var r) && !string.IsNullOrWhiteSpace(r)
                ? r
                : entry.Event == LogEvents.GmJail ? "admin" : null;
            result[id] = (Utc(entry.Timestamp), reason);
        }

        return result;
    }

    private static bool MatchesLeaderboardFilter(ushort points, string name, string? color, string? search)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            string filter = color.Trim().ToLowerInvariant();
            if (filter == "black" && points < Pking.BlackNamePoints) return false;
            if (filter == "red" && (points < Pking.RedNamePoints || points >= Pking.BlackNamePoints)) return false;
        }
        if (!string.IsNullOrWhiteSpace(search) && !name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string ColorForPoints(ushort points) =>
        points >= Pking.BlackNamePoints ? "black" :
        points >= Pking.RedNamePoints ? "red" : "white";

    private static string? MapName(CatalogStore catalogs, uint mapId) =>
        catalogs.MapsById.TryGetValue(mapId, out var map) ? map.Name : null;

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

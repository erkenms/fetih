namespace Comet.Web.Features.ItemHistory;

using Comet.Game.Database;
using Comet.Web.Auth;
using Comet.Web.Features.Dashboard;
using Comet.Web.Features.Items;
using Comet.Web.Features.Logs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class ItemHistoryFeature : IFeature
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 1000;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(90);

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/item-history").RequirePermission(Permission.ViewLogs);
        group.MapGet("/{itemId}", GetItemHistoryAsync)
            .WithName("getItemHistory")
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        group.MapGet("/duplicates", GetDuplicatesAsync)
            .WithName("getItemDuplicates")
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ItemHistoryResponse>, JsonHttpResult<ApiError>>> GetItemHistoryAsync(
        uint itemId,
        ILokiClient loki,
        ObservabilityOptions options,
        ItemCatalog itemCatalog,
        DateTime? from = null,
        DateTime? to = null,
        int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        if (itemId == 0)
            return ApiError.Result("invalid_item_id", StatusCodes.Status400BadRequest);

        DateTime endUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
        DateTime startUtc = from?.ToUniversalTime() ?? endUtc - DefaultWindow;
        if (startUtc >= endUtc || endUtc - startUtc > MaxRange)
            return ApiError.Result("invalid_time_range", StatusCodes.Status400BadRequest);

        int clampedLimit = Math.Clamp(limit, 1, MaxLimit);
        string query = ItemHistoryParser.BuildItemLogQl(itemId);
        string grafanaExploreUrl = LogQl.BuildGrafanaExploreUrl(options.GrafanaUrl, options.ScopeLogs(query), startUtc, endUtc);

        await using var db = new ServerDbContext();

        var liveRows = await (
            from i in db.Items.AsNoTracking()
            where i.ItemID == itemId
            join c in db.Characters.AsNoTracking() on i.CharacterID equals c.CharacterID into cg
            from c in cg.DefaultIfEmpty()
            select new
            {
                i.ItemID,
                i.TypeID,
                i.CharacterID,
                CharacterName = c != null ? c.Name : null,
                i.Position,
                i.Package,
                i.StackAmount,
                i.Plus,
                i.Bless,
                i.Gem1,
                i.Gem2,
                i.Enchant,
                i.Locked,
            }).ToArrayAsync(ct);

        var currentHolders = liveRows.Select(r =>
        {
            itemCatalog.TryGet(r.TypeID, out var cat);
            return new ItemHolderInfo(
                r.ItemID,
                r.TypeID,
                cat?.Name,
                cat?.Icon,
                r.CharacterID,
                r.CharacterName,
                r.Position,
                r.Package,
                r.StackAmount,
                r.Plus,
                r.Bless,
                r.Gem1,
                r.Gem2,
                r.Enchant,
                r.Locked);
        }).ToArray();

        bool lokiAvailable = true;
        string? lokiError = null;
        bool truncated = false;
        LogEntry[] rawEntries = [];

        try
        {
            rawEntries = await loki.QueryRangeAsync(query, startUtc, endUtc, clampedLimit, ct);
            truncated = rawEntries.Length >= clampedLimit;
        }
        catch (LokiUnavailableException)
        {
            lokiAvailable = false;
            lokiError = "loki_unavailable";
        }

        var matched = rawEntries
            .Where(e => ItemHistoryParser.MatchesItem(e, itemId))
            .OrderBy(e => e.Timestamp)
            .ToArray();

        var namesToResolve = ItemHistoryParser.CollectCharacterNames(matched);
        var characterIdsByName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (namesToResolve.Count > 0)
        {
            var resolved = await db.Characters.AsNoTracking()
                .Where(c => namesToResolve.Contains(c.Name))
                .Select(c => new { c.Name, c.CharacterID })
                .ToArrayAsync(ct);
            foreach (var r in resolved)
                characterIdsByName[r.Name] = r.CharacterID;
        }

        var steps = matched
            .Select(e => ItemHistoryParser.ToStep(e, itemId, characterIdsByName))
            .ToArray();

        var duplicates = ItemHistoryParser.DetectDuplicates(itemId, currentHolders, steps);

        uint? typeId = currentHolders.FirstOrDefault()?.TypeId
            ?? steps.LastOrDefault(s => s.TypeId.HasValue)?.TypeId;
        string? itemName = currentHolders.FirstOrDefault()?.ItemName;
        string? itemIcon = currentHolders.FirstOrDefault()?.ItemIcon;
        if (typeId.HasValue && (itemName == null || itemIcon == null) && itemCatalog.TryGet(typeId.Value, out var catalogItem) && catalogItem != null)
        {
            itemName ??= catalogItem.Name;
            itemIcon ??= catalogItem.Icon;
        }

        return TypedResults.Ok(new ItemHistoryResponse(
            itemId,
            typeId,
            itemName,
            itemIcon,
            currentHolders,
            steps,
            duplicates,
            lokiAvailable,
            lokiError,
            query,
            grafanaExploreUrl,
            startUtc,
            endUtc,
            truncated));
    }

    private static async Task<Results<Ok<ItemDuplicatesResponse>, JsonHttpResult<ApiError>>> GetDuplicatesAsync(
        ILokiClient loki,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        DateTime endUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
        DateTime startUtc = from?.ToUniversalTime() ?? endUtc.AddDays(-7);
        if (startUtc >= endUtc || endUtc - startUtc > MaxRange)
            return ApiError.Result("invalid_time_range", StatusCodes.Status400BadRequest);

        var flags = new List<ItemDuplicateFlag>();
        await using var db = new ServerDbContext();

        // 1. Live items sharing the same ItemID in the database
        var dupIds = await db.Items.AsNoTracking()
            .GroupBy(i => i.ItemID)
            .Where(g => g.Count() > 1)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .Take(100)
            .ToArrayAsync(ct);

        foreach (var d in dupIds)
        {
            var charIds = await db.Items.AsNoTracking()
                .Where(i => i.ItemID == d.ItemId)
                .Select(i => i.CharacterID)
                .Distinct()
                .ToArrayAsync(ct);
            flags.Add(new ItemDuplicateFlag(
                "duplicate_live_id",
                $"{d.Count} live rows share item ID {d.ItemId}.",
                d.ItemId,
                charIds,
                []));
        }

        // 2. Live items with multiple non-stack item.created events in Loki
        bool lokiAvailable = true;
        string? lokiError = null;
        try
        {
            var createdEntries = await loki.QueryRangeAsync(
                ItemHistoryParser.BuildCreatedLogQl(), startUtc, endUtc, MaxLimit, ct);

            var grouped = createdEntries
                .Where(e => !e.Fields.ContainsKey("stackAmount"))
                .Select(e => (Entry: e, ItemId: ItemHistoryParser.TryGetUInt(e.Fields, "itemId")))
                .Where(x => x.ItemId.HasValue && x.ItemId.Value > 0)
                .GroupBy(x => x.ItemId!.Value)
                .Where(g => g.Count() > 1)
                .ToArray();

            if (grouped.Length > 0)
            {
                var candidateIds = grouped.Select(g => g.Key).ToArray();
                var liveItems = await db.Items.AsNoTracking()
                    .Where(i => candidateIds.Contains(i.ItemID))
                    .Select(i => new { i.ItemID, i.CharacterID })
                    .ToArrayAsync(ct);
                var liveSet = liveItems.GroupBy(i => i.ItemID).ToDictionary(g => g.Key, g => g.Select(x => x.CharacterID).Distinct().ToArray());

                foreach (var g in grouped)
                {
                    if (!liveSet.TryGetValue(g.Key, out var holders))
                        continue;

                    var charIds = holders
                        .Concat(g.Where(x => x.Entry.CharacterId.HasValue).Select(x => x.Entry.CharacterId!.Value))
                        .Distinct()
                        .ToArray();

                    flags.Add(new ItemDuplicateFlag(
                        "multiple_creation_events",
                        $"Live item {g.Key} has {g.Count()} separate item.created events.",
                        g.Key,
                        charIds,
                        g.Select(x => x.Entry.Timestamp).Order().ToArray()));
                }
            }
        }
        catch (LokiUnavailableException)
        {
            lokiAvailable = false;
            lokiError = "loki_unavailable";
        }

        return TypedResults.Ok(new ItemDuplicatesResponse(flags.ToArray(), lokiAvailable, lokiError, startUtc, endUtc));
    }
}

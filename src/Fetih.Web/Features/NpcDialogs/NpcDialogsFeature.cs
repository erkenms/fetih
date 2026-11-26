namespace Fetih.Web.Features.NpcDialogs;

using System.Globalization;
using Fetih.Web.Auth;
using Fetih.Web.Features.Logs;
using Microsoft.AspNetCore.Http.HttpResults;

public class NpcDialogsFeature : IFeature
{
    // Every click on an NPC and every answer: {"npcId": 45, "option": 1, "characterId": ...}
    private const string TalkSelector = "{server=\"game\", event=\"npc.talk\"}";

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<NpcDialogsStore>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/npc-dialogs").RequirePermission(Permission.ViewCatalogs);
        group.MapGet("", ListAsync).WithName("listNpcDialogs");
        group.MapGet("/{npcId}", GetAsync)
            .WithName("getNpcDialog")
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<NpcDialogListResponse>> ListAsync(
        NpcDialogsStore store, IMetricRanges metrics, string range = "7d", CancellationToken ct = default)
    {
        (string key, TimeSpan window) = NormalizeRange(range);
        Dictionary<uint, int>? talks = null;
        try
        {
            var byNpc = await InstantAsync(metrics,
                $"sum by (npcId) (count_over_time({TalkSelector} | json npcId [{Duration(window)}]))", "npcId", window, ct);
            talks = byNpc.Where(e => uint.TryParse(e.Key, CultureInfo.InvariantCulture, out _))
                .ToDictionary(e => uint.Parse(e.Key, CultureInfo.InvariantCulture), e => e.Value);
        }
        catch (MetricsUnavailableException)
        {
        }
        return TypedResults.Ok(new NpcDialogListResponse(store.List(talks), talks != null, key));
    }

    private static async Task<Results<Ok<NpcDialogDetailResponse>, NotFound<ApiError>>> GetAsync(
        uint npcId, NpcDialogsStore store, IMetricRanges metrics, ILokiClient loki, string range = "7d",
        CancellationToken ct = default)
    {
        (_, TimeSpan window) = NormalizeRange(range);
        var detail = await store.GetAsync(npcId, await StatsAsync(metrics, loki, npcId, window, ct));
        return detail == null ? TypedResults.NotFound(new ApiError("not_found")) : TypedResults.Ok(detail);
    }

    private static async Task<NpcTalkStats> StatsAsync(
        IMetricRanges metrics, ILokiClient loki, uint npcId, TimeSpan window, CancellationToken ct)
    {
        string selector = $"{TalkSelector} | json npcId, option, characterId | npcId=\"{npcId}\"";
        string over = Duration(window);
        try
        {
            var byOption = InstantAsync(metrics, $"sum by (option) (count_over_time({selector} [{over}]))", "option", window, ct);
            var players = InstantAsync(metrics,
                $"count(sum by (characterId) (count_over_time({selector} [{over}])))", "", window, ct);
            var last = loki.QueryRangeAsync(selector, DateTime.UtcNow - window, DateTime.UtcNow, 1, ct);
            var options = await byOption;
            return new NpcTalkStats(true, options.Values.Sum(), (await players).GetValueOrDefault(""),
                (await last).FirstOrDefault()?.Timestamp, options);
        }
        catch (Exception e) when (e is MetricsUnavailableException or LokiUnavailableException)
        {
            return new NpcTalkStats(false, 0, 0, null, []);
        }
    }

    // A metric query's value now, per label value
    private static async Task<Dictionary<string, int>> InstantAsync(
        IMetricRanges metrics, string query, string label, TimeSpan window, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var series = await metrics.LokiAsync(query, label, now, now, window, ct);
        return series.ToDictionary(s => s.Key, s => s.Value.Length == 0 ? 0 : (int)s.Value[^1].Value);
    }

    private static string Duration(TimeSpan window) => $"{(int)window.TotalHours}h";

    private static (string Key, TimeSpan Window) NormalizeRange(string? range) =>
        range switch
        {
            "24h" => ("24h", TimeSpan.FromHours(24)),
            _ => ("7d", TimeSpan.FromDays(7)),
        };
}

namespace Comet.Web.Features.Logs;

using Comet.Web.Auth;
using Comet.Web.Features.Dashboard;
using Microsoft.AspNetCore.Http.HttpResults;

public record TimelineResponse(
    LogEntry[] Entries,
    string Query,
    string GrafanaUrl,
    string GrafanaExploreUrl,
    DateTime From,
    DateTime To,
    bool Truncated);

public class LogsFeature : IFeature
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(30);

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddHttpClient<ILokiClient, LokiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/logs").RequirePermission(Permission.ViewLogs);
        group.MapGet("/timeline", QueryTimelineAsync)
            .WithName("queryLogTimeline")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/characters/{characterId}", QueryCharacterAsync)
            .WithName("characterTimeline")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/accounts/{accountId}", QueryAccountAsync)
            .WithName("accountTimeline")
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);
    }

    private static Task<Results<Ok<TimelineResponse>, JsonHttpResult<ApiError>>> QueryTimelineAsync(
        ILokiClient loki,
        ObservabilityOptions options,
        uint? characterId,
        uint? accountId,
        string? server,
        string? level,
        string? eventPrefix,
        string? search,
        bool includePackets = false,
        DateTime? from = null,
        DateTime? to = null,
        int limit = DefaultLimit,
        CancellationToken ct = default) =>
        RunQueryAsync(loki, options, new LogQueryFilter(characterId, accountId, server, level, eventPrefix, search, includePackets),
            from, to, limit, ct);

    private static Task<Results<Ok<TimelineResponse>, JsonHttpResult<ApiError>>> QueryCharacterAsync(
        uint characterId,
        ILokiClient loki,
        ObservabilityOptions options,
        uint? accountId,
        string? server,
        string? level,
        string? eventPrefix,
        string? search,
        bool includePackets = false,
        DateTime? from = null,
        DateTime? to = null,
        int limit = DefaultLimit,
        CancellationToken ct = default) =>
        RunQueryAsync(loki, options, new LogQueryFilter(characterId, accountId, server, level, eventPrefix, search, includePackets),
            from, to, limit, ct);

    private static Task<Results<Ok<TimelineResponse>, JsonHttpResult<ApiError>>> QueryAccountAsync(
        uint accountId,
        ILokiClient loki,
        ObservabilityOptions options,
        string? server,
        string? level,
        string? eventPrefix,
        string? search,
        bool includePackets = false,
        DateTime? from = null,
        DateTime? to = null,
        int limit = DefaultLimit,
        CancellationToken ct = default) =>
        RunQueryAsync(loki, options, new LogQueryFilter(null, accountId, server, level, eventPrefix, search, includePackets),
            from, to, limit, ct);

    private static async Task<Results<Ok<TimelineResponse>, JsonHttpResult<ApiError>>> RunQueryAsync(
        ILokiClient loki,
        ObservabilityOptions options,
        LogQueryFilter filter,
        DateTime? from,
        DateTime? to,
        int limit,
        CancellationToken ct)
    {
        DateTime endUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
        DateTime startUtc = from?.ToUniversalTime() ?? endUtc.AddHours(-24);
        if (startUtc >= endUtc || endUtc - startUtc > MaxRange)
            return ApiError.Result("invalid_time_range", StatusCodes.Status400BadRequest);

        int clampedLimit = Math.Clamp(limit, 1, MaxLimit);
        string query = LogQl.BuildQuery(filter);
        string grafanaUrl = LogQl.BuildGrafanaUrl(options.GrafanaUrl, filter, startUtc, endUtc);
        string grafanaExploreUrl = LogQl.BuildGrafanaExploreUrl(options.GrafanaUrl, query, startUtc, endUtc);

        try
        {
            var entries = await loki.QueryRangeAsync(query, startUtc, endUtc, clampedLimit, ct);
            return TypedResults.Ok(new TimelineResponse(
                entries, query, grafanaUrl, grafanaExploreUrl, startUtc, endUtc, entries.Length >= clampedLimit));
        }
        catch (LokiUnavailableException)
        {
            return ApiError.Result("loki_unavailable", StatusCodes.Status503ServiceUnavailable);
        }
    }
}

namespace Fetih.Web.Features.PublicSite;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;

public class PublicSiteFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddMemoryCache();
        services.AddSingleton<GitNewsService>();
        services.AddSingleton<PublicSiteService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var pub = api.MapGroup("/public").AllowAnonymous();
        pub.MapGet("/home", async (PublicSiteService site, CancellationToken ct) =>
            TypedResults.Ok(await site.GetHomeAsync(ct))).WithName("getPublicHome");
        pub.MapGet("/rankings/level", async (PublicSiteService site, int? baseClass = null, int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetLevelRankingAsync(baseClass, page, pageSize))).WithName("getLevelRanking");
        pub.MapGet("/rankings/nobility", async (PublicSiteService site, int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetNobilityRankingAsync(page, pageSize))).WithName("getNobilityRanking");
        pub.MapGet("/rankings/guilds", async (PublicSiteService site, string sort = "fund", int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetGuildRankingAsync(sort, page, pageSize))).WithName("getGuildRanking");
        pub.MapGet("/rankings/arena", async (PublicSiteService site, string kind = "points", int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetArenaRankingAsync(kind, page, pageSize))).WithName("getArenaRanking");
        pub.MapGet("/rankings/arena/seasons", async (PublicSiteService site) =>
            TypedResults.Ok(await site.GetArenaSeasonsAsync())).WithName("getArenaSeasons");
        pub.MapGet("/rankings/arena/seasons/{season}", async (PublicSiteService site, uint season, int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetArenaSeasonRankingAsync(season, page, pageSize))).WithName("getArenaSeasonRanking");
        pub.MapGet("/rankings/flowers", async (PublicSiteService site, string kind = "redRose", int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetFlowerRankingAsync(kind, page, pageSize))).WithName("getFlowerRanking");
        pub.MapGet("/rankings/pk", async (PublicSiteService site, int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetPkRankingAsync(page, pageSize))).WithName("getPkRanking");
        pub.MapGet("/rankings/families", async (PublicSiteService site, int page = 1, int pageSize = PublicSiteService.DefaultPageSize) =>
            TypedResults.Ok(await site.GetFamilyRankingAsync(page, pageSize))).WithName("getFamilyRanking");
        pub.MapGet("/characters/{id}", GetCharacterAsync).WithName("getPublicCharacter");
        pub.MapGet("/guilds/{id}", GetGuildAsync).WithName("getPublicGuild");
        pub.MapGet("/news", async (PublicSiteService site, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
            TypedResults.Ok(await site.GetNewsAsync(page, pageSize, ct))).WithName("getPublicNews");
        pub.MapGet("/download", (PublicSiteService site) =>
            TypedResults.Ok(site.GetDownloadInfo())).WithName("getDownloadInfo");
    }

    internal static Task<PublicSiteService.NobilityBoardCache> GetCachedNobilityBoardAsync(IMemoryCache cache) =>
        PublicSiteService.GetCachedNobilityBoardAsync(cache);

    private static async Task<Results<Ok<PublicCharacterResponse>, NotFound<ApiError>>> GetCharacterAsync(
        PublicSiteService site, uint id)
    {
        var ch = await site.GetCharacterAsync(id);
        return ch == null ? TypedResults.NotFound(new ApiError("not_found")) : TypedResults.Ok(ch);
    }

    private static async Task<Results<Ok<PublicGuildResponse>, NotFound<ApiError>>> GetGuildAsync(
        PublicSiteService site, uint id)
    {
        var guild = await site.GetGuildAsync(id);
        return guild == null ? TypedResults.NotFound(new ApiError("not_found")) : TypedResults.Ok(guild);
    }
}

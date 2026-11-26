namespace Fetih.Web.Features.PublicHeatmap;

using Microsoft.AspNetCore.Http.HttpResults;

public class PublicHeatmapFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<IHeatmapGameSource, RpcHeatmapGameSource>();
        services.AddSingleton<PublicHeatmapService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var pub = api.MapGroup("/public").AllowAnonymous();
        pub.MapGet("/heatmap", GetHeatmapAsync).WithName("getPublicHeatmap");
    }

    private static async Task<Ok<PublicHeatmapResponse>> GetHeatmapAsync(
        PublicHeatmapService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(ct));
}

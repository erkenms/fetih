namespace Fetih.Web.Features.Live;

using System.Text.Json.Serialization;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public class LiveFeature : IFeature
{
    public const string HubPath = "/hubs/live";

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.AddSingleton<ILiveGameSource, RpcLiveGameSource>();
        services.AddSingleton<LiveWorldService>();
        services.AddHostedService(sp => sp.GetRequiredService<LiveWorldService>());
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/live").RequirePermission(Permission.ViewLive);

        group.MapGet("/online", GetOnlineAsync).WithName("getLiveOnline");
        group.MapGet("/maps/{id:long}", GetMapSnapshotAsync).WithName("getLiveMapSnapshot");
    }

    public void MapRoot(IEndpointRouteBuilder app)
    {
        app.MapHub<LiveHub>(HubPath).RequirePermission(Permission.ViewLive);
    }

    private static async Task<Ok<OnlinePlayersResponse>> GetOnlineAsync(LiveWorldService live) =>
        TypedResults.Ok(await live.FetchOnlinePlayersAsync());

    private static async Task<Results<Ok<LiveMapSnapshot>, BadRequest<ApiError>>> GetMapSnapshotAsync(
        LiveWorldService live, long id)
    {
        if (id is < 0 or > uint.MaxValue)
            return TypedResults.BadRequest(new ApiError("invalid_map_id"));

        return TypedResults.Ok(await live.FetchMapSnapshotAsync((uint)id));
    }
}

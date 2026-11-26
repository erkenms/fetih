namespace Fetih.Web.Features.Broadcasts;

using System.Security.Claims;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class BroadcastsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<BroadcastsDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
        services.AddSingleton<BroadcastService>();
        services.AddSingleton<AnnouncementScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<AnnouncementScheduler>());
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/broadcasts").RequirePermission(Permission.Moderate);
        group.MapGet("/queue", GetQueueAsync).WithName("getBroadcastQueue");
        group.MapPost("/send", SendAsync).WithName("sendBroadcast");
        group.MapGet("/announcements", async (BroadcastService service) => TypedResults.Ok(await service.ListAsync()))
            .WithName("listAnnouncements");
        group.MapPost("/announcements", CreateAnnouncementAsync).WithName("createAnnouncement");
        group.MapPut("/announcements/{id:int}", UpdateAnnouncementAsync).WithName("updateAnnouncement");
        group.MapDelete("/announcements/{id:int}", DeleteAnnouncementAsync).WithName("deleteAnnouncement");
        group.MapPost("/announcements/{id:int}/send", SendAnnouncementNowAsync).WithName("sendAnnouncementNow");
    }

    private static async Task<Ok<BroadcastQueueResponse>> GetQueueAsync(BroadcastService service) =>
        TypedResults.Ok(new BroadcastQueueResponse(service.GameConnected, await service.QueueAsync()));

    private static async Task<Results<Ok<SendBroadcastResponse>, JsonHttpResult<ApiError>>> SendAsync(
        SendBroadcastRequest request, ClaimsPrincipal user, BroadcastService service)
    {
        if (await service.SendAsync(user, request.Channel, request.Text) is { } error)
            return ApiError.Result(error, StatusCodes.Status400BadRequest);
        return TypedResults.Ok(new SendBroadcastResponse(true, request.Channel, request.Text.Trim()));
    }

    private static async Task<Results<Created<AnnouncementDto>, JsonHttpResult<ApiError>>> CreateAnnouncementAsync(
        SaveAnnouncementRequest request, ClaimsPrincipal user, BroadcastService service)
    {
        var (saved, error) = await service.SaveAsync(user, null, request);
        if (saved == null)
            return ApiError.Result(error!, StatusCodes.Status400BadRequest);
        return TypedResults.Created($"/api/broadcasts/announcements/{saved.Id}", saved);
    }

    private static async Task<Results<Ok<AnnouncementDto>, JsonHttpResult<ApiError>>> UpdateAnnouncementAsync(
        uint id, SaveAnnouncementRequest request, ClaimsPrincipal user, BroadcastService service)
    {
        var (saved, error) = await service.SaveAsync(user, id, request);
        return saved != null ? TypedResults.Ok(saved) : Error(error!);
    }

    private static async Task<Results<NoContent, JsonHttpResult<ApiError>>> DeleteAnnouncementAsync(
        uint id, BroadcastService service) =>
        await service.DeleteAsync(id) is { } error ? Error(error) : TypedResults.NoContent();

    private static async Task<Results<Ok<AnnouncementDto>, JsonHttpResult<ApiError>>> SendAnnouncementNowAsync(
        uint id, ClaimsPrincipal user, BroadcastService service)
    {
        var (sent, error) = await service.SendNowAsync(user, id);
        return sent != null ? TypedResults.Ok(sent) : Error(error!);
    }

    private static JsonHttpResult<ApiError> Error(string error) => ApiError.Result(error,
        error == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
}

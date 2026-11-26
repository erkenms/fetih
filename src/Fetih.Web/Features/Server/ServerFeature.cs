namespace Fetih.Web.Features.Server;

using System.Security.Claims;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public class ServerFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<ServerControlDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
        services.AddSingleton<ServerControlService>();
        services.AddSingleton<Board.BoardService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/server").RequirePermission(Permission.ServerControl);
        group.MapGet("/status", async (ServerControlService service) => TypedResults.Ok(await service.StatusAsync()))
            .WithName("getServerControlStatus");
        group.MapPost("/shutdown", StartShutdownAsync).WithName("startServerShutdown");
        group.MapPost("/shutdown/cancel", CancelShutdownAsync).WithName("cancelServerShutdown");
        group.MapPut("/maintenance", UpdateMaintenanceAsync).WithName("updateServerMaintenance");
        group.MapPut("/motd", UpdateMotdAsync).WithName("updateServerMotd");
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> StartShutdownAsync(
        StartShutdownRequest request, ClaimsPrincipal user, ServerControlService service)
    {
        if (await service.StartShutdownAsync(user, request.Mode, request.Seconds, request.Reason) is { } error)
            return ApiError.Result(error, error == "shutdown_pending" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        return TypedResults.Ok(new ServerActionResponse(true, true));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> CancelShutdownAsync(
        ClaimsPrincipal user, ServerControlService service)
    {
        if (await service.CancelShutdownAsync(user) is { } error)
            return ApiError.Result(error, error == "no_shutdown_pending" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        return TypedResults.Ok(new ServerActionResponse(true, true));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> UpdateMaintenanceAsync(
        UpdateMaintenanceRequest request, ClaimsPrincipal user, ServerControlService service)
    {
        var (appliedLive, error) = await service.UpdateMaintenanceAsync(user, request.Enabled, request.Message, request.KickNonGm);
        if (error != null)
            return ApiError.Result(error, StatusCodes.Status400BadRequest);
        return TypedResults.Ok(new ServerActionResponse(true, appliedLive));
    }

    private static async Task<Results<Ok<ServerActionResponse>, JsonHttpResult<ApiError>>> UpdateMotdAsync(
        UpdateMotdRequest request, ClaimsPrincipal user, ServerControlService service)
    {
        var (appliedLive, error) = await service.UpdateMotdAsync(user, request.Text);
        if (error != null)
            return ApiError.Result(error, StatusCodes.Status400BadRequest);
        return TypedResults.Ok(new ServerActionResponse(true, appliedLive));
    }
}

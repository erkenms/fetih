namespace Comet.Web.Features.DataEdit;

using Comet.Game.DataEditing;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public sealed class DataEditFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<DataEditService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/data").RequirePermission(Permission.Studio);

        group.MapGet("/levels", (DataEditService svc) => TypedResults.Ok(svc.ReadLevels()))
            .WithName("getLevelsData");
        group.MapPut("/levels", SaveLevelsAsync)
            .WithName("saveLevelsData");

        group.MapGet("/goals", (DataEditService svc) => TypedResults.Ok(svc.ReadGoals()))
            .WithName("getGoalsData");
        group.MapPut("/goals", SaveGoalsAsync)
            .WithName("saveGoalsData");

        group.MapGet("/teleporters", (DataEditService svc) => TypedResults.Ok(svc.ReadTeleporters()))
            .WithName("getTeleportersData");
        group.MapPut("/teleporters", SaveTeleportersAsync)
            .WithName("saveTeleportersData");

        group.MapGet("/talkers", (DataEditService svc) => TypedResults.Ok(svc.ReadTalkers()))
            .WithName("getTalkersData");
        group.MapPut("/talkers", SaveTalkersAsync)
            .WithName("saveTalkersData");

        group.MapGet("/maps/{mapId}/info", (uint mapId, DataEditService svc) => TypedResults.Ok(svc.GetMapPickInfo(mapId)))
            .WithName("getDataMapInfo");
        group.MapGet("/maps/{mapId}/cell", (uint mapId, int x, int y, DataEditService svc) => TypedResults.Ok(svc.CheckCell(mapId, x, y)))
            .WithName("checkDataMapCell");
    }

    private static Task<Results<Ok<SaveDataResponse>, Conflict<ApiError>>> SaveLevelsAsync(
        SaveLevelsRequest request, DataEditService svc, AuditLog audit, CancellationToken ct) =>
        RunSaveAsync(() => svc.SaveLevelsAsync(request, audit, ct));

    private static Task<Results<Ok<SaveDataResponse>, Conflict<ApiError>>> SaveGoalsAsync(
        SaveGoalsRequest request, DataEditService svc, AuditLog audit, CancellationToken ct) =>
        RunSaveAsync(() => svc.SaveGoalsAsync(request, audit, ct));

    private static Task<Results<Ok<SaveDataResponse>, Conflict<ApiError>>> SaveTeleportersAsync(
        SaveTeleportersRequest request, DataEditService svc, AuditLog audit, CancellationToken ct) =>
        RunSaveAsync(() => svc.SaveTeleportersAsync(request, audit, ct));

    private static Task<Results<Ok<SaveDataResponse>, Conflict<ApiError>>> SaveTalkersAsync(
        SaveTalkersRequest request, DataEditService svc, AuditLog audit, CancellationToken ct) =>
        RunSaveAsync(() => svc.SaveTalkersAsync(request, audit, ct));

    private static async Task<Results<Ok<SaveDataResponse>, Conflict<ApiError>>> RunSaveAsync(
        Func<Task<SaveDataResponse>> save)
    {
        try
        {
            return TypedResults.Ok(await save());
        }
        catch (DataFileConflictException)
        {
            return TypedResults.Conflict(new ApiError("conflict"));
        }
    }
}

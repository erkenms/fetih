namespace Comet.Web.Features.MapEdit;

using Comet.Game.DataEditing;
using Comet.Shared.Models;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;

public sealed class MapEditFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MapEditService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/maps/{id:long}/edit").RequirePermission(Permission.Studio);

        group.MapGet("/", GetMapEdit).WithName("getMapEdit");
        group.MapPut("/", SaveMapEditAsync).WithName("saveMapEdit");
        group.MapPost("/reload", ReloadMapFilesAsync).WithName("reloadMapFiles");
    }

    private static Results<Ok<MapEditResponse>, NotFound<ApiError>> GetMapEdit(MapEditService svc, long id) =>
        id is >= 0 and <= uint.MaxValue
            ? TypedResults.Ok(svc.ReadMap((uint)id))
            : TypedResults.NotFound(new ApiError("not_found"));

    private static async Task<Results<Ok<SaveMapEditResponse>, NotFound<ApiError>, Conflict<ApiError>>> SaveMapEditAsync(
        long id,
        SaveMapEditRequest request,
        MapEditService svc,
        AuditLog audit,
        CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return TypedResults.NotFound(new ApiError("not_found"));

        try
        {
            return TypedResults.Ok(await svc.SaveMapAsync((uint)id, request, audit, ct));
        }
        catch (DataFileConflictException)
        {
            return TypedResults.Conflict(new ApiError("conflict"));
        }
    }

    private static async Task<Results<Ok<ReloadMapFilesResponse>, BadRequest<ApiError>>> ReloadMapFilesAsync(
        long id,
        ReloadMapFilesRequest request,
        GameRpc gameRpc,
        AuditLog audit)
    {
        var requested = request.Files is { Length: > 0 }
            ? request.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : MapEditService.MapDataFiles;

        var valid = new HashSet<string>(MapEditService.MapDataFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var file in requested)
        {
            if (!valid.Contains(file))
                return TypedResults.BadRequest(new ApiError("invalid_file"));
        }

        var results = new List<ReloadMapFileResult>(requested.Length);
        foreach (var file in requested)
        {
            var result = await StudioData.ReloadFileAsync(file, gameRpc, audit);

            results.Add(new ReloadMapFileResult(
                result.File ?? file,
                result.Applied,
                [.. result.Errors],
                [.. result.Warnings],
                result.Summary));
        }

        return TypedResults.Ok(new ReloadMapFilesResponse([.. results]));
    }
}

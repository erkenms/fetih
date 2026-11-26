namespace Fetih.Web.Features.Integrity;

using Fetih.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public class IntegrityFeature : IFeature
{
    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/integrity").RequirePermission(Permission.Studio);
        group.MapGet("/", GetReport).WithName("getIntegrityReport");
    }

    private static Ok<IntegrityReportResponse> GetReport(WebConfiguration config, ClientFiles client) =>
        TypedResults.Ok(IntegrityChecker.Run(config, client));
}

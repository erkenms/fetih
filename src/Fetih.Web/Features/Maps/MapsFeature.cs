namespace Fetih.Web.Features.Maps;

using Fetih.Web.Auth;

public class MapsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MapDataStore>();
        services.AddSingleton<MapTileService>();
    }

    public void MapRoot(IEndpointRouteBuilder app)
    {
        app.MapGet("/assets/maps/tiles/{key}/{layer}/{z:int}/{x:int}/{y:int}.webp",
            async (string key, string layer, int z, int x, int y, MapTileService tiles, HttpResponse response) =>
            {
                var bytes = await tiles.GetTileAsync(key, layer, z, x, y);
                if (bytes == null)
                    return Results.NotFound();
                response.Headers.CacheControl = "public, max-age=86400";
                return Results.File(bytes, "image/webp");
            }).RequireAuthorization(Permissions.Policy(Permission.ViewCatalogs));
    }
}

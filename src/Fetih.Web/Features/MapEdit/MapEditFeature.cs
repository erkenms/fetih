namespace Fetih.Web.Features.MapEdit;

public sealed class MapEditFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MapEditService>();
    }
}

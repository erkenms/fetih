namespace Fetih.Web.Features.Maps;

public class MapsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MapDataStore>();
    }
}

namespace Fetih.Web.Features.MonsterEdit;

public class MonsterEditFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MonsterDataService>();
    }
}

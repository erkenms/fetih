namespace Fetih.Web.Features.NpcDialogs;

public class NpcDialogsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<NpcDialogsStore>();
    }
}

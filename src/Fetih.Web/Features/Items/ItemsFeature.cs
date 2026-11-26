namespace Fetih.Web.Features.Items;

public class ItemsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<ItemCatalog>();
    }
}

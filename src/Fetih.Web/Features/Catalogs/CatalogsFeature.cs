namespace Fetih.Web.Features.Catalogs;

public class CatalogsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<CatalogStore>();
    }
}

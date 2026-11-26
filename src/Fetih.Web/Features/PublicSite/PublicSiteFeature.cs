namespace Fetih.Web.Features.PublicSite;

public class PublicSiteFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddMemoryCache();
        services.AddSingleton<GitNewsService>();
        services.AddSingleton<PublicSiteService>();
    }
}

namespace Fetih.Web.Features.Changes;

public class ChangesFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<GitDataService>();
    }
}

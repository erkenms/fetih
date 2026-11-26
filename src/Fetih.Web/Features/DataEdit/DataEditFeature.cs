namespace Fetih.Web.Features.DataEdit;

public sealed class DataEditFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<DataEditService>();
    }
}

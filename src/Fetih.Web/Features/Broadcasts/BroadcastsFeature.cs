namespace Fetih.Web.Features.Broadcasts;

using Microsoft.EntityFrameworkCore;

public class BroadcastsFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<BroadcastsDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
        services.AddSingleton<BroadcastService>();
        services.AddSingleton<AnnouncementScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<AnnouncementScheduler>());
    }
}

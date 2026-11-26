namespace Fetih.Web.Features.Server;

using Microsoft.EntityFrameworkCore;

public class ServerFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddDbContextFactory<ServerControlDbContext>(options => options.UseMySql(
            config.Database!.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
        services.AddSingleton<ServerControlService>();
        services.AddSingleton<Board.BoardService>();
    }
}

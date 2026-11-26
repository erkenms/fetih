namespace Fetih.Web.Features.Players;

public sealed class PlayersFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config) =>
        services.AddSingleton<PlayerService>();
}

namespace Fetih.Web.Features.Dashboard;

using Fetih.Shared.Models.Admin;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public record DashboardCounts(int Accounts, int AccountsLastDay, int Characters, int CharactersLastDay);

public record DashboardResponse(
    DateTime ServerTime,
    // Null while the game server is offline
    ServerState? Game,
    // Accounts with a character in game, to mark the recent logins that are still on
    uint[] OnlineAccounts,
    DashboardCounts Counts,
    // Null when Loki can't be reached
    LogStatsResponse? Logs,
    string GrafanaUrl);

public class DashboardFeature : IFeature
{
    public const int RecentCount = 12;
    public static readonly TimeSpan ErrorWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan LoginWindow = TimeSpan.FromDays(1);

    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton(sp =>
        {
            var options = new ObservabilityOptions();
            sp.GetRequiredService<IConfiguration>().GetSection("Observability").Bind(options);
            return options;
        });
        services.AddHttpClient<LogStats>(client => client.Timeout = TimeSpan.FromSeconds(3));
    }

    public void Map(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", GetAsync).WithName("dashboard").RequirePermission(Permission.AdminArea);
    }

    private static async Task<Ok<DashboardResponse>> GetAsync(
        GameRpc game, LogStats logs, ObservabilityOptions options, CancellationToken ct)
    {
        var gameTask = GameAsync(game);
        var logsTask = logs.GetAsync(ErrorWindow, LoginWindow, RecentCount, ct);
        var counts = await CountsAsync(ct);
        var (status, online) = await gameTask;
        return TypedResults.Ok(new DashboardResponse(DateTime.UtcNow, status, online, counts,
            await logsTask, options.GrafanaUrl.TrimEnd('/')));
    }

    private static async Task<(ServerState?, uint[])> GameAsync(GameRpc game)
    {
        if (!game.Connected)
            return (null, []);
        try
        {
            var status = await game.CallAsync<ServerState>("ServerStatus");
            var players = await game.CallAsync<List<OnlinePlayer>>("OnlinePlayers");
            return (status, players.Select(p => p.AccountID).Distinct().Order().ToArray());
        }
        catch (GameOfflineException)
        {
            return (null, []);
        }
    }

    // Both databases keep times in UTC
    private static async Task<DashboardCounts> CountsAsync(CancellationToken ct)
    {
        var dayAgo = DateTime.UtcNow - LoginWindow;
        await using var accounts = new Fetih.Account.Database.ServerDbContext();
        await using var characters = new Fetih.Game.Database.ServerDbContext();
        return new DashboardCounts(
            await accounts.Accounts.CountAsync(ct),
            await accounts.Accounts.CountAsync(a => a.Registered >= dayAgo, ct),
            await characters.Characters.CountAsync(ct),
            await characters.Characters.CountAsync(c => c.Registered >= dayAgo, ct));
    }
}

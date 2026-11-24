namespace Comet.Web.Features.Health;

using Comet.Web.Audit;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

// The example feature: one endpoint, its own response type, and a page in
// web/src/features/health. Copy this folder to start a new feature.
public record HealthResponse(bool GameDatabase, bool AccountDatabase, bool GameServer, DateTime ServerTime);

public class HealthFeature : IFeature
{
    public void Map(RouteGroupBuilder api)
    {
        api.MapGet("/health", GetAsync).WithName("health").AllowAnonymous();
    }

    private static async Task<Ok<HealthResponse>> GetAsync(
        IDbContextFactory<WebDbContext> factory, GameRpc game)
    {
        await using var db = await factory.CreateDbContextAsync();
        bool gameDb = await CanConnectAsync(() => db.Database.CanConnectAsync());
        bool accountDb = await CanConnectAsync(async () =>
        {
            await using var accounts = new Comet.Account.Database.ServerDbContext();
            return await accounts.Database.CanConnectAsync();
        });
        return TypedResults.Ok(new HealthResponse(gameDb, accountDb, game.Connected, DateTime.UtcNow));
    }

    private static async Task<bool> CanConnectAsync(Func<Task<bool>> probe)
    {
        try { return await probe(); }
        catch { return false; }
    }
}

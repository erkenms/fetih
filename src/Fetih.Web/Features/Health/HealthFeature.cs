namespace Fetih.Web.Features.Health;

using Fetih.Web.Audit;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

public record HealthResponse(bool GameDatabase, bool AccountDatabase, bool GameServer, DateTime ServerTime);

public class HealthFeature : IFeature
{
    public void Map(RouteGroupBuilder api)
    {
        api.MapGet("/health", GetAsync).WithName("health").AllowAnonymous();
    }

    // /healthz for monitoring: "ok", or 503 with what's down
    public void MapRoot(IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (IDbContextFactory<WebDbContext> factory, GameRpc game) =>
        {
            var health = (await GetAsync(factory, game)).Value!;
            string[] down =
            [
                .. health.GameDatabase ? [] : new[] { "game database" },
                .. health.AccountDatabase ? [] : new[] { "account database" },
                .. health.GameServer ? [] : new[] { "game server" },
            ];
            return down.Length == 0 ? Results.Text("ok")
                : Results.Text("down: " + string.Join(", ", down), statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();
    }

    private static async Task<Ok<HealthResponse>> GetAsync(
        IDbContextFactory<WebDbContext> factory, GameRpc game)
    {
        await using var db = await factory.CreateDbContextAsync();
        bool gameDb = await CanConnectAsync(() => db.Database.CanConnectAsync());
        bool accountDb = await CanConnectAsync(async () =>
        {
            await using var accounts = new Fetih.Account.Database.ServerDbContext();
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

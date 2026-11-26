namespace Fetih.Web.Features.Health;

using Fetih.Web.Audit;
using Fetih.Web.Game;
using Microsoft.EntityFrameworkCore;

public class HealthFeature : IFeature
{
    // /healthz for monitoring: "ok", or 503 with what's down
    public void MapRoot(IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (IDbContextFactory<WebDbContext> factory, GameRpc game) =>
        {
            await using var db = await factory.CreateDbContextAsync();
            bool gameDb = await CanConnectAsync(() => db.Database.CanConnectAsync());
            bool accountDb = await CanConnectAsync(async () =>
            {
                await using var accounts = new Fetih.Account.Database.ServerDbContext();
                return await accounts.Database.CanConnectAsync();
            });
            string[] down =
            [
                .. gameDb ? [] : new[] { "game database" },
                .. accountDb ? [] : new[] { "account database" },
                .. game.Connected ? [] : new[] { "game server" },
            ];
            return down.Length == 0 ? Results.Text("ok")
                : Results.Text("down: " + string.Join(", ", down), statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();
    }

    private static async Task<bool> CanConnectAsync(Func<Task<bool>> probe)
    {
        try { return await probe(); }
        catch { return false; }
    }
}

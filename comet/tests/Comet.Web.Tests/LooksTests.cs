namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.Looks;
using Xunit;

[Collection(WebCollection.Name)]
public class LooksTests(WebFixture web)
{
    [Fact]
    public void LookStore_NamesNpcLooksByTypeAndMonsterLooksByTheMonstersUsingThem()
    {
        var assets = Path.Combine(Path.GetTempPath(), "comet-looks-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(assets);
        try
        {
            File.WriteAllText(Path.Combine(assets, "looks.json"), """
                {
                  "previewDirection": 0,
                  "npc": {
                    "10": { "image": "looks/npc/10.png", "x": 40, "y": 160 },
                    "9736": { "image": "looks/npc/9736.png", "x": 50, "y": 170 }
                  },
                  "monster": {
                    "104": { "image": "looks/monster/104.png", "x": 60, "y": 120 },
                    "900": { "image": "looks/monster/900.png", "x": 30, "y": 150 }
                  }
                }
                """);
            var store = new LookStore(assets,
                () => new Dictionary<int, string> { [1] = "Storekeeper" },
                () => [Monster(2, "Pheasant", 104, 150), Monster(1, "Turkey", 104), Monster(3, "Ghost", 999)]);

            var looks = store.Looks;
            Assert.Equal([10u, 9736u], looks.Npc.Select(n => n.Lookface));
            Assert.Equal("Storekeeper", looks.Npc[0].Name);
            Assert.Null(looks.Npc[1].Name);
            Assert.Equal((40, 160), (looks.Npc[0].X, looks.Npc[0].Y));
            Assert.Equal("looks/monster/104.png", looks.Monster[0].Image);
            Assert.Equal(["Turkey", "Pheasant"], looks.Monster[0].Monsters.Select(m => m.Name));
            Assert.Equal([100, 150], looks.Monster[0].Monsters.Select(m => m.ZoomPercent));
            Assert.Empty(looks.Monster[1].Monsters);
        }
        finally
        {
            Directory.Delete(assets, recursive: true);
        }
    }

    [Fact]
    public void LookStore_IsEmptyWithoutTheAssets()
    {
        var store = new LookStore("/nonexistent/assets", () => new Dictionary<int, string>(), () => []);
        Assert.Empty(store.Looks.Npc);
        Assert.Empty(store.Looks.Monster);
    }

    [Fact]
    public async Task LooksEndpoint_NeedsCatalogPermission()
    {
        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/catalogs/looks")).StatusCode);

        var playerEmail = await web.CreateAccountAsync(authority: 1);
        using var player = await web.LoginAsync(playerEmail, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/catalogs/looks")).StatusCode);

        var modEmail = await web.CreateAccountAsync(authority: 2);
        using var mod = await web.LoginAsync(modEmail, "secret1");
        var looks = await mod.GetFromJsonAsync<LooksCatalogResponse>("/api/catalogs/looks");
        Assert.NotNull(looks);
    }

    private static MonsterCatalogEntry Monster(uint id, string name, uint lookface, int? zoom = null) => new(
        id, name, lookface, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        false, null, null, InServer: true, InClient: true, null, null, null, zoom);
}

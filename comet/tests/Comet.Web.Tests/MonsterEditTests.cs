namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Comet.Game.States;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.MonsterEdit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

[Collection(WebCollection.Name)]
public class MonsterEditTests(WebFixture web)
{
    private static MonsterDto SampleMonster(uint id, string name = "TestGoblin") => new(
        Id: id,
        Name: name,
        Lookface: 104,
        Level: 10,
        Life: 250,
        AttackMin: 12,
        AttackMax: 18,
        Defense: 5,
        Dexterity: 3,
        Dodge: 40,
        AttackRange: 1,
        ViewRange: 8,
        AttackSpeed: 500,
        MoveSpeed: 500,
        AttackUser: 3,
        DropMoney: 25,
        DropHp: 1000000,
        DropMp: 1001000,
        Boss: false,
        MagicType: 0,
        MagicHitRate: 100,
        Spells: []);

    private static string CreateTempDataDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "comet-monster-edit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "monsters.json"), """
            // Monster types header comment
            {
              "monsters": [
                {"id": 1, "name": "Pheasant", "lookface": 104, "level": 1, "life": 33, "attackMin": 5, "attackMax": 6, "defense": 0, "dexterity": 0, "dodge": 36, "attackRange": 1, "viewRange": 1, "attackSpeed": 500, "moveSpeed": 500, "attackUser": 3, "dropMoney": 5, "dropHp": 1000000, "dropMp": 1001000, "boss": false},
                {"id": 2, "name": "UnusedMob", "lookface": 200, "level": 5, "life": 100, "attackMin": 10, "attackMax": 12, "defense": 0, "dexterity": 1, "dodge": 30, "attackRange": 1, "viewRange": 6, "attackSpeed": 500, "moveSpeed": 500, "attackUser": 3, "dropMoney": 10, "dropHp": 0, "dropMp": 0, "boss": false}
              ]
            }
            """);
        File.WriteAllText(Path.Combine(dir, "spawns.json"), """
            {
              "spawns": [
                {"id": 1, "map": 1002, "x": 382, "y": 456, "spread": 15, "amount": 10, "respawn": 10, "monster": 1}
              ]
            }
            """);
        File.WriteAllText(Path.Combine(dir, "bosses.json"), """
            {
              "bosses": []
            }
            """);
        File.WriteAllText(Path.Combine(dir, "drops.json"), """
            {
              "drops": []
            }
            """);
        return dir;
    }

    private async Task<(HttpClient Client, IDisposable Factory)> CreatePmClientAsync(string tempDir)
    {
        var factory = web.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<MonsterDataService>();
                services.AddSingleton(sp => new MonsterDataService(
                    tempDir,
                    sp.GetRequiredService<WebConfiguration>(),
                    sp.GetRequiredService<CatalogStore>()));
            });
        });

        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = pmUser, password = "secret1" });
        login.EnsureSuccessStatusCode();
        return (client, factory);
    }

    [Fact]
    public async Task Endpoints_RequireStudioPermission()
    {
        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/studio/monsters")).StatusCode);

        string playerUser = await web.CreateAccountAsync((ushort)Authority.Player);
        using var player = await web.LoginAsync(playerUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/studio/monsters")).StatusCode);

        string gmUser = await web.CreateAccountAsync((ushort)Authority.GameMaster);
        using var gm = await web.LoginAsync(gmUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.GetAsync("/api/studio/monsters")).StatusCode);

        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");
        var ok = await pm.GetAsync("/api/studio/monsters");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var body = await ok.Content.ReadFromJsonAsync<MonsterListResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Monsters);
        Assert.NotEmpty(body.Hash);
    }

    [Fact]
    public async Task CrudAndValidation_RoundTripPreservesCommentsAndChecksReferences()
    {
        string tempDir = CreateTempDataDir();
        try
        {
            var (pm, factory) = await this.CreatePmClientAsync(tempDir);
            using (factory)
            using (pm)
            {
                // List includes spawn counts
                var list = await pm.GetFromJsonAsync<MonsterListResponse>("/api/studio/monsters");
                Assert.NotNull(list);
                Assert.Equal(2, list.Monsters.Length);
                var pheasant = Assert.Single(list.Monsters, r => r.Monster.Id == 1);
                Assert.Equal(1, pheasant.SpawnAreaCount);
                Assert.Equal(10, pheasant.TotalSpawnAmount);

                // Detail includes spawn refs
                var detail = await pm.GetFromJsonAsync<MonsterDetailResponse>("/api/studio/monsters/1");
                Assert.NotNull(detail);
                Assert.Single(detail.Spawns);
                Assert.Equal(1002u, detail.Spawns[0].MapId);

                // Create invalid monster (attackMin > attackMax) fails 400
                var invalid = SampleMonster(99) with { AttackMin = 50, AttackMax = 10 };
                var badRes = await pm.PostAsJsonAsync("/api/studio/monsters", new SaveMonsterRequest(list.Hash, invalid));
                Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);

                // Create valid monster succeeds 201
                var createdReq = SampleMonster(99, "FrostWolf") with { Spells = [1001, 1002] };
                var createRes = await pm.PostAsJsonAsync("/api/studio/monsters", new SaveMonsterRequest(list.Hash, createdReq));
                Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
                var created = await createRes.Content.ReadFromJsonAsync<SaveMonsterResponse>();
                Assert.NotNull(created);
                Assert.Equal("FrostWolf", created.Monster.Name);
                Assert.Equal(new ushort[] { 1001, 1002 }, created.Monster.Spells);

                // Stale hash returns 409 Conflict
                var staleRes = await pm.PutAsJsonAsync("/api/studio/monsters/99", new SaveMonsterRequest(list.Hash, createdReq with { Life = 300 }));
                Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);

                // Update with current hash succeeds
                var updateRes = await pm.PutAsJsonAsync("/api/studio/monsters/99", new SaveMonsterRequest(created.Hash, createdReq with { Life = 300, Boss = true }));
                Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
                var updated = await updateRes.Content.ReadFromJsonAsync<SaveMonsterResponse>();
                Assert.NotNull(updated);
                Assert.Equal(300u, updated.Monster.Life);
                Assert.True(updated.Monster.Boss);

                // Header comment is preserved on disk
                string diskText = await File.ReadAllTextAsync(Path.Combine(tempDir, "monsters.json"));
                Assert.StartsWith("// Monster types header comment", diskText);

                // Cannot delete monster #1 because spawns.json references it
                var deleteInUse = await pm.DeleteAsync($"/api/studio/monsters/1?hash={updated.Hash}");
                Assert.Equal(HttpStatusCode.BadRequest, deleteInUse.StatusCode);

                // Can delete monster #99 (unused)
                var deleteOk = await pm.DeleteAsync($"/api/studio/monsters/99?hash={updated.Hash}");
                Assert.Equal(HttpStatusCode.OK, deleteOk.StatusCode);

                // Reload returns 503 when game server is offline
                var reloadRes = await pm.PostAsync("/api/studio/monsters/reload", null);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, reloadRes.StatusCode);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

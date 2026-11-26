namespace Fetih.Web.Tests;

using System.Net;
using Fetih.Game.States;
using Fetih.Web.Audit;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.MapEdit;
using Fetih.Web.Features.Maps;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class MapsPageTests(WebFixture fixture)
{
    [Fact]
    public async Task SignedOutGoesToSignIn()
    {
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var list = await client.GetAsync("/admin/maps");
        Assert.Equal(HttpStatusCode.Redirect, list.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fmaps", list.Headers.Location?.PathAndQuery);

        var detail = await client.GetAsync("/admin/maps/1002");
        Assert.Equal(HttpStatusCode.Redirect, detail.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fmaps%2F1002", detail.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task PlayerIsForbidden()
    {
        var client = await fixture.SignInAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/maps")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/maps/1002")).StatusCode);
    }

    [Fact]
    public async Task ModeratorSeesListAndReadOnlyMapAndCannotEdit()
    {
        string email = await fixture.CreateAccountAsync((ushort)Authority.Moderator);
        var client = await fixture.SignInAsync(email, "secret1");

        string listHtml = await client.GetStringAsync("/admin/maps?q=1002");
        Assert.Contains("href=\"/admin/maps/1002\"", listHtml);

        string detailHtml = await client.GetStringAsync("/admin/maps/1002");
        Assert.Contains("id=\"map-data\"", detailHtml);
        Assert.Contains("\"canEdit\":false", detailHtml);
        Assert.Contains("\"singleFigure\":true", detailHtml);
        Assert.DoesNotContain("data-place=\"npc\"", detailHtml);
        Assert.DoesNotContain("Oyuna yükle", detailHtml);

        var moveResp = await WebFixture.PostFormAsync(client, "/admin/maps/1002?handler=Move", new()
        {
            ["kind"] = "npc", ["entityId"] = "1", ["x"] = "400", ["y"] = "400",
        });
        Assert.Equal(HttpStatusCode.Forbidden, moveResp.StatusCode);

        var reloadResp = await WebFixture.PostFormAsync(client, "/admin/maps/1002?handler=Reload", new());
        Assert.Equal(HttpStatusCode.Forbidden, reloadResp.StatusCode);
    }

    [Fact]
    public async Task StudioUserSeesEditControlsAndSidePanels()
    {
        string email = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await fixture.SignInAsync(email, "secret1");

        string detailHtml = await client.GetStringAsync("/admin/maps/1002");
        Assert.Contains("\"canEdit\":true", detailHtml);
        Assert.Contains("data-place=\"npc\"", detailHtml);
        Assert.Contains("Oyuna yükle", detailHtml);

        var editData = fixture.Factory.Services.GetRequiredService<MapEditService>().ReadMap(1002);

        // NPC panel includes dialog text
        var npc = Assert.Single(editData.Npcs.Take(1));
        string npcPanel = await client.GetStringAsync($"/admin/maps/1002?handler=Panel&type=npc&entityId={npc.Id}");
        Assert.Contains("Konuşma", npcPanel);
        Assert.Contains($"value=\"{npc.Id}\"", npcPanel);

        // Portal panel links to target map
        var portal = editData.Portals.First(p => p.ToMap.HasValue);
        string portalPanel = await client.GetStringAsync($"/admin/maps/1002?handler=Panel&type=portal&entityId={portal.Index}");
        Assert.Contains($"href=\"/admin/maps/{portal.ToMap}\"", portalPanel);

        // Guard single-figure spawn panel
        var guardSpawn = editData.Spawns.First(s => s.Spread == 0 && s.Amount == 1);
        string spawnPanel = await client.GetStringAsync($"/admin/maps/1002?handler=Panel&type=spawn&entityId={guardSpawn.Id}");
        Assert.Contains("Tek figür", spawnPanel);
    }

    [Fact]
    public async Task StudioReadOnlyBlocksEditingForEveryone()
    {
        string email = await fixture.CreateAccountAsync((ushort)Authority.Administrator);
        var client = await fixture.SignInAsync(email, "secret1");
        var config = fixture.Factory.Services.GetRequiredService<WebConfiguration>();

        bool previous = config.Web.StudioReadOnly;
        config.Web.StudioReadOnly = true;
        try
        {
            string html = await client.GetStringAsync("/admin/maps/1002");
            Assert.Contains("\"canEdit\":false", html);
            Assert.DoesNotContain("data-place=\"npc\"", html);

            var moveResp = await WebFixture.PostFormAsync(client, "/admin/maps/1002?handler=Move", new()
            {
                ["kind"] = "entry", ["entityId"] = "0", ["x"] = "430", ["y"] = "380",
            });
            Assert.Equal(HttpStatusCode.Forbidden, moveResp.StatusCode);
        }
        finally
        {
            config.Web.StudioReadOnly = previous;
        }
    }

    [Fact]
    public async Task ReloadWhenGameOfflineFlashesError()
    {
        string email = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await fixture.SignInAsync(email, "secret1");

        var resp = await WebFixture.PostFormAsync(client, "/admin/maps/1002?handler=Reload", new());
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        string html = await client.GetStringAsync("/admin/maps/1002");
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", html);
    }

    [Fact]
    public async Task SingleChangeEditMethodsWriteAndValidate()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "fetih-mapspage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "maps.json"), """
                {
                  "maps": [
                    { "map": 1000, "name": "desert_city", "portal": { "x": 499, "y": 650 }, "reborn": 1000 },
                    { "map": 1002, "name": "twin_city", "flags": "PkDisable", "portal": { "x": 430, "y": 380 }, "reborn": 1002 }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tmp, "npcs.json"), """
                {
                  "npcs": [
                    { "id": 20, "lookface": 30, "role": 2, "map": 1002, "x": 430, "y": 380 }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tmp, "spawns.json"), """
                {
                  "spawns": [
                    { "id": 4, "map": 1002, "x": 445, "y": 435, "spread": 0, "amount": 1, "respawn": 10, "monster": 900 }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tmp, "portals.json"), """
                {
                  "portals": [
                    { "map": 1002, "portal": 2, "toMap": 1000, "toX": 499, "toY": 650 }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tmp, "monsters.json"), """
                {
                  "monsters": [
                    { "id": 900, "name": "Guard1", "life": 1000, "attackMin": 50, "attackMax": 100 }
                  ]
                }
                """);

            var cfg = new WebConfiguration { Paths = new WebConfiguration.PathsSection { DataDirectory = tmp } };
            var catalogs = new CatalogStore(cfg);
            var client = new ClientFiles(cfg);
            var svc = new MapEditService(cfg, catalogs, new MapDataStore(cfg, catalogs, client), client);

            using var scope = fixture.Factory.Services.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();

            // Move NPC
            var movedNpc = await svc.MoveNpcAsync(1002, 20, 432, 384, audit, CancellationToken.None);
            Assert.True(movedNpc.Saved);
            Assert.Equal((ushort)432, svc.ReadMap(1002).Npcs.Single(n => n.Id == 20).X);

            // Add & delete NPC
            var addedNpc = await svc.SaveNpcAsync(1002, null, new EditableNpc(0, 40, 2, 410, 410, 0, 0), audit, CancellationToken.None);
            Assert.True(addedNpc.Saved);
            Assert.Equal(21u, addedNpc.EntityId);
            Assert.Equal(2, svc.ReadMap(1002).Npcs.Length);

            var deletedNpc = await svc.DeleteNpcAsync(1002, 21, audit, CancellationToken.None);
            Assert.True(deletedNpc.Saved);
            Assert.Single(svc.ReadMap(1002).Npcs);

            // Move guard spawn
            var movedSpawn = await svc.MoveSpawnAsync(1002, 4, 446, 436, audit, CancellationToken.None);
            Assert.True(movedSpawn.Saved);
            Assert.Equal((ushort)446, svc.ReadMap(1002).Spawns.Single(s => s.Id == 4).X);

            // Validation refusal on bad monster ID
            var badSpawn = await svc.SaveSpawnAsync(1002, null, new EditableSpawn(0, 400, 400, 5, 3, 10, 999999), audit, CancellationToken.None);
            Assert.False(badSpawn.Saved);
            Assert.NotEmpty(badSpawn.Problems);
            Assert.Single(svc.ReadMap(1002).Spawns);

            // Save & delete portal
            var addedPortal = await svc.SavePortalAsync(1002, null, 5, 1000, 500, 650, audit, CancellationToken.None);
            Assert.True(addedPortal.Saved);
            Assert.Equal(2, svc.ReadMap(1002).Portals.Length);

            var deletedPortal = await svc.DeletePortalAsync(1002, 5, audit, CancellationToken.None);
            Assert.True(deletedPortal.Saved);
            Assert.Single(svc.ReadMap(1002).Portals);

            // Move entry point
            var movedEntry = await svc.MoveEntryPointAsync(1002, 431, 381, audit, CancellationToken.None);
            Assert.True(movedEntry.Saved);
            Assert.Equal(new MapCellPoint(431, 381), svc.ReadMap(1002).Settings.Portal);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}

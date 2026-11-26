namespace Fetih.Web.Tests;

using Fetih.Game.DataEditing;
using Fetih.Game.World;
using Fetih.Web.Audit;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.MapEdit;
using Fetih.Web.Features.Maps;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class MapEditTests(WebFixture web)
{
    [Fact]
    public void ApplyScopedEntries_PreservesOtherMapsAndComments()
    {
        const string source = """
            // npcs header
            {
              "npcs": [
                { "id": 100, "lookface": 20, "role": 2, "map": 1000, "x": 10, "y": 20 }, // Desert NPC
                { "id": 200, "lookface": 30, "role": 2, "map": 1002, "x": 400, "y": 350 }, // TC NPC 1
                { "id": 201, "lookface": 40, "role": 3, "map": 1002, "x": 410, "y": 360 } // TC NPC 2
              ]
            }
            """;

        var file = DataFile<Npc>.Parse(source, "npcs");
        var nextFor1002 = new List<Npc>
        {
            new() { NpcID = 200, Lookface = 35, Role = 2, MapID = 1002, X = 405, Y = 355 },
            new() { NpcID = 202, Lookface = 50, Role = 2, MapID = 1002, X = 420, Y = 370 },
        };

        var (added, updated, removed) = MapEditService.ApplyScopedEntries(
            file,
            n => n.MapID == 1002,
            nextFor1002,
            n => n.NpcID,
            Npc.Same);

        Assert.Equal((1, 1, 1), (added, updated, removed));
        Assert.Contains("// npcs header", file.Text);
        Assert.Contains("// Desert NPC", file.Text);
        Assert.Equal(3, file.Count);
        Assert.Equal(100u, file[0].NpcID);
        Assert.Equal(1000u, file[0].MapID);
        Assert.Equal(200u, file[1].NpcID);
        Assert.Equal((ushort)405, file[1].X);
        Assert.Equal(202u, file[2].NpcID);
    }

    [Fact]
    public async Task SaveMapEdit_InTempDirectory_WritesChangesAndPreservesOtherMaps()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "fetih-mapedit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "maps.json"), """
                // maps header
                {
                  "maps": [
                    { "map": 1000, "name": "desert_city", "flags": "NeverWound", "portal": { "x": 499, "y": 650 }, "reborn": 1000 },
                    { "map": 1002, "name": "twin_city", "aliases": ["tc"], "flags": "PkDisable", "portal": { "x": 430, "y": 380 }, "reborn": 1002 }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tmp, "npcs.json"), """
                {
                  "npcs": [
                    { "id": 10, "lookface": 20, "role": 2, "map": 1000, "x": 100, "y": 100 },
                    { "id": 20, "lookface": 30, "role": 2, "map": 1002, "x": 430, "y": 380 }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tmp, "spawns.json"), """
                {
                  "spawns": [
                    { "id": 1, "map": 1002, "x": 380, "y": 450, "spread": 15, "amount": 10, "respawn": 10, "monster": 1 }
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
                    { "id": 1, "name": "Pheasant", "life": 100, "attackMin": 5, "attackMax": 10 }
                  ]
                }
                """);

            var cfg = new WebConfiguration
            {
                Paths = new WebConfiguration.PathsSection { DataDirectory = tmp },
            };
            var catalogs = new CatalogStore(cfg);
            var client = new ClientFiles(cfg);
            var svc = new MapEditService(cfg, catalogs, new MapDataStore(cfg, catalogs, client), client);

            var read = svc.ReadMap(1002);
            Assert.Equal("twin_city", read.Settings.Name);
            Assert.Single(read.Npcs);
            Assert.Single(read.Spawns);
            Assert.Single(read.Portals);
            Assert.Equal(21u, read.NextNpcId);
            Assert.Equal(2u, read.NextSpawnId);

            using var scope = web.Factory.Services.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();

            var saveReq = new SaveMapEditRequest(
                read.Hashes,
                read.Settings with { Flags = ["PkDisable", "BoothEnable"], Portal = new MapCellPoint(435, 385) },
                [
                    read.Npcs[0] with { X = 432, Y = 382 },
                    new EditableNpc(21, 50, 3, 440, 390, 0, 0),
                ],
                [
                    read.Spawns[0] with { Spread = 20, Amount = 12 },
                ],
                [
                    read.Portals[0] with { ToX = 500, ToY = 651 },
                ]);

            var saved = await svc.SaveMapAsync(1002, saveReq, audit, CancellationToken.None);
            Assert.True(saved.Saved);
            Assert.Equal(4, saved.ChangedFiles.Length);
            Assert.True(saved.Counts.MapsChanged);
            Assert.Equal(1, saved.Counts.NpcsAdded);
            Assert.Equal(1, saved.Counts.NpcsUpdated);
            Assert.Equal(1, saved.Counts.SpawnsUpdated);
            Assert.Equal(1, saved.Counts.PortalsUpdated);

            // Re-read and verify both 1002 edits and untouched 1000 entries + header comment
            var after = svc.ReadMap(1002);
            Assert.Contains("BoothEnable", after.Settings.Flags);
            Assert.Equal(new MapCellPoint(435, 385), after.Settings.Portal);
            Assert.Equal(2, after.Npcs.Length);
            Assert.Equal((ushort)20, after.Spawns[0].Spread);
            Assert.Equal((ushort)500, after.Portals[0].ToX);

            string mapsText = File.ReadAllText(Path.Combine(tmp, "maps.json"));
            Assert.Contains("// maps header", mapsText);
            Assert.Contains("desert_city", mapsText);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}

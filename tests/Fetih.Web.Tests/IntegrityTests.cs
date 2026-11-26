namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Fetih.Game.DataEditing;
using Fetih.Game.States;
using Fetih.Game.World;
using Fetih.Web.Features.Integrity;
using Xunit;
using Xunit.Abstractions;

[Collection(WebCollection.Name)]
public class IntegrityTests(WebFixture web, ITestOutputHelper output)
{
    // 20x20 grid with a wall at x = 10 and portal 0 at (2, 2)
    private static readonly MapGrid TestGrid = MapGrid.Parse(MapsTests.BuildDMap(
        20, 20, (x, y) => x != 10, (_, _) => 0, [(2, 2, 0)]));

    [Fact]
    public void DetectsBrokenReferencesAcrossAllDataFiles()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "fetih-integrity-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "monsters.json"), """
                { "monsters": [
                  { "id": 1, "name": "Pheasant", "life": 33, "attackMin": 5, "attackMax": 6 }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "maps.json"), """
                { "maps": [
                  { "map": 1002, "name": "twin_city", "portal": { "x": 5, "y": 5 } }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "npcs.json"), """
                { "npcs": [
                  { "id": 100, "map": 1002, "x": 5, "y": 5 },
                  { "id": 101, "map": 1002, "x": 10, "y": 5 }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "spawns.json"), """
                { "spawns": [
                  { "id": 1, "map": 1002, "x": 5, "y": 5, "spread": 2, "amount": 3, "monster": 9999 },
                  { "id": 2, "map": 9999, "x": 5, "y": 5, "spread": 2, "amount": 3, "monster": 1 }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "bosses.json"), """
                { "bosses": [
                  { "id": "bad-boss", "monster": 8888, "map": 8888, "x": 5, "y": 5, "times": ["12:00"] }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "drops.json"), """
                { "drops": [
                  { "monster": 1, "entries": [ { "item": 777777, "chance": 50, "count": 1 } ] }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "portals.json"), """
                { "portals": [
                  { "map": 1002, "portal": 0, "toMap": 1002, "toX": 10, "toY": 5 },
                  { "map": 1002, "portal": 1, "toMap": 9998, "toX": 5, "toY": 5 }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "teleporters.json"), """
                { "teleporters": [
                  { "id": 100, "destinations": [ { "name": "Wall", "map": 1002, "x": 10, "y": 5 }, { "name": "Void", "map": 9997, "x": 5, "y": 5 } ] },
                  { "id": 5555, "destinations": [ { "name": "Ok", "map": 1002, "x": 5, "y": 5 } ] }
                ] }
                """);
            File.WriteAllText(Path.Combine(tmp, "talkers.json"), """
                { "talkers": [
                  { "id": 100, "text": ["Hello"], "reply": "Hi" },
                  { "id": 4444, "text": ["Ghost"], "reply": "Bye" }
                ] }
                """);

            var report = IntegrityChecker.Run(new DataSet(tmp, id => id == 1002 ? TestGrid : null, id => id == 1088000));

            Assert.True(report.ClientFilesAvailable);
            Assert.Contains(report.Problems, p => p.File == "spawns.json" && p.Category == "missing_monster" && p.EditorPath == "/admin/maps/1002?x=5&y=5");
            Assert.Contains(report.Problems, p => p.File == "spawns.json" && p.Category == "missing_map");
            Assert.Contains(report.Problems, p => p.File == "bosses.json" && p.Category == "missing_monster");
            Assert.Contains(report.Problems, p => p.File == "bosses.json" && p.Category == "missing_map");
            Assert.Contains(report.Problems, p => p.File == "drops.json" && p.Category == "missing_item" && p.EditorPath == "/admin/changes");
            Assert.Contains(report.Problems, p => p.File == "portals.json" && p.Category == "unwalkable_cell");
            Assert.Contains(report.Problems, p => p.File == "portals.json" && p.Category == "missing_map");
            Assert.Contains(report.Problems, p => p.File == "teleporters.json" && p.Category == "unwalkable_cell" && p.EditorPath == "/admin/data?tab=teleporters");
            Assert.Contains(report.Problems, p => p.File == "teleporters.json" && p.Category == "missing_map");
            Assert.Contains(report.Problems, p => p.File == "teleporters.json" && p.Category == "missing_npc");
            Assert.Contains(report.Problems, p => p.File == "talkers.json" && p.Category == "missing_npc" && p.Entry == 1);
            Assert.DoesNotContain(report.Problems, p => p.File == "npcs.json");
            Assert.Contains(report.Problems, p => p.File == "talkers.json" && p.Category == "missing_npc" && p.EditorPath == "/admin/data?tab=talkers");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void SourceDataHasNoIntegrityErrors()
    {
        string dataDir = StudioData.ResolveDataDirectory(new WebConfiguration());
        string? clientDir = FindRealClientDir();
        var report = IntegrityChecker.Run(clientDir == null ? new DataSet(dataDir) : DataSet.ForClient(dataDir, clientDir));

        output.WriteLine($"ClientAvailable={report.ClientFilesAvailable} Errors={report.ErrorCount} Warnings={report.WarningCount}");
        foreach (var g in report.Problems.GroupBy(p => (p.File, p.Category, p.Level)))
            output.WriteLine($"  {g.Key.File} [{g.Key.Level}/{g.Key.Category}]: {g.Count()} (e.g. {g.First().Message})");

        var errors = report.Problems
            .Where(p => p.Level == "error")
            .Select(p => $"{p.File}:{p.Line} [{p.Category}] {p.Message}")
            .ToArray();

        Assert.Empty(errors);
    }

    [Fact]
    public async Task IntegrityEndpoint_RequiresStudioPermission()
    {
        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/integrity")).StatusCode);

        string playerUser = await web.CreateAccountAsync((ushort)Authority.Player);
        using var player = await web.LoginAsync(playerUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/integrity")).StatusCode);

        string gmUser = await web.CreateAccountAsync((ushort)Authority.GameMaster);
        using var gm = await web.LoginAsync(gmUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.GetAsync("/api/integrity")).StatusCode);

        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");
        var response = await pm.GetAsync("/api/integrity");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<IntegrityReportResponse>();
        Assert.NotNull(report);
        Assert.Equal(0, report.ErrorCount);
        Assert.NotEmpty(report.Files);
    }

    private static string? FindRealClientDir()
    {
        var env = Environment.GetEnvironmentVariable("FETIH_TEST_CLIENT_DIR");
        if (!string.IsNullOrEmpty(env)
            && File.Exists(Path.Combine(env, "ini", "GameMap.dat"))
            && File.Exists(Path.Combine(env, "ini", "itemtype.dat")))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "_CLIENT", "fetih");
            if (File.Exists(Path.Combine(candidate, "ini", "GameMap.dat"))
                && File.Exists(Path.Combine(candidate, "ini", "itemtype.dat")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

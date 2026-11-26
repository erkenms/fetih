namespace Fetih.Web.Tests;

using Fetih.Game.DataEditing;
using Fetih.Game.Npcs;
using Fetih.Game.States;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.DataEdit;
using Xunit;

public class DataEditTests
{
    [Fact]
    public void ApplyEntries_UpdatesMinimalDiffAndPreservesComments()
    {
        const string source = """
            // header comment
            {
              "talkers": [
                {
                  "id": 100, "face": 1, // First NPC
                  "text": ["Hello"],
                  "reply": "Hi.",
                },
                {
                  "id": 200, "face": 2, // Second NPC
                  "text": ["Bye"],
                  "reply": "See ya.",
                },
              ]
            }
            """;

        var file = DataFile<Talker>.Parse(source, "talkers");
        var next = new List<Talker>
        {
            new() { NpcID = 100, Face = 1, Text = ["Hello"], Reply = "Hi." },
            new() { NpcID = 200, Face = 9, Text = ["Bye"], Reply = "See ya." },
            new() { NpcID = 300, Face = 5, Text = ["New"], Reply = "Ok." },
        };

        var (added, updated, removed) = DataEditService.ApplyEntries(
            file,
            next,
            t => t.NpcID,
            (a, b) => a.NpcID == b.NpcID && a.Face == b.Face && a.Reply == b.Reply
                && (a.Text ?? []).SequenceEqual(b.Text ?? []));

        Assert.Equal((1, 1, 0), (added, updated, removed));
        Assert.Contains("// header comment", file.Text);
        Assert.Contains("// First NPC", file.Text);
        Assert.Contains("// Second NPC", file.Text);
        Assert.Equal(3, file.Count);
        Assert.Equal((byte)9, file[1].Face);
        Assert.Equal(300u, file[2].NpcID);
    }

    [Fact]
    public void ApplyEntries_HandlesRemovals()
    {
        const string source = """
            {
              "levels": [
                { "level": 1, "experience": 59, "upLevTime": 2 },
                { "level": 2, "experience": 195, "upLevTime": 2 },
                { "level": 3, "experience": 203, "upLevTime": 1 }
              ]
            }
            """;

        var file = DataFile<LevelExp>.Parse(source, "levels");
        var next = new List<LevelExp>
        {
            new() { Level = 1, Experience = 59, UpLevTime = 2 },
            new() { Level = 3, Experience = 250, UpLevTime = 1 },
        };

        var (added, updated, removed) = DataEditService.ApplyEntries(
            file,
            next,
            l => l.Level,
            (a, b) => a.Level == b.Level && a.Experience == b.Experience && a.UpLevTime == b.UpLevTime);

        Assert.Equal((0, 1, 1), (added, updated, removed));
        Assert.Equal(2, file.Count);
        Assert.Equal(250UL, file[1].Experience);
    }

    [Fact]
    public void ReadFromTempDirectory_LoadsAndValidatesAllFourFiles()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "fetih-dataedit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "levels.json"), """{ "levels": [ { "level": 1, "experience": 59, "upLevTime": 2 } ] }""");
            File.WriteAllText(Path.Combine(tmp, "goals.json"), """{ "npcs": [ { "id": 100011, "lookface": 427, "role": 21, "level": 20, "life": 10000, "map": 1039, "x": 20, "y": 220 } ] }""");
            File.WriteAllText(Path.Combine(tmp, "teleporters.json"), """{ "teleporters": [ { "id": 10050, "fare": 100, "face": 1, "text": ["Hi"], "destinations": [ { "name": "TC", "map": 1002, "x": 430, "y": 380 } ] } ] }""");
            File.WriteAllText(Path.Combine(tmp, "talkers.json"), """{ "talkers": [ { "id": 4573, "face": 67, "text": ["Hello"], "reply": "Bye." } ] }""");

            var cfg = new WebConfiguration
            {
                Paths = new WebConfiguration.PathsSection { DataDirectory = tmp },
            };
            var svc = new DataEditService(cfg, new CatalogStore(cfg));

            Assert.Single(svc.ReadLevels().Entries);
            Assert.Single(svc.ReadGoals().Entries);
            Assert.Single(svc.ReadTeleporters().Entries);
            Assert.Single(svc.ReadTalkers().Entries);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}

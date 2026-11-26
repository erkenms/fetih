namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Fetih.Game.DataEditing;
using Fetih.Game.Npcs;
using Fetih.Game.States;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.DataEdit;
using Xunit;

[Collection(WebCollection.Name)]
public class DataEditTests(WebFixture web)
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
    public async Task DataEndpoints_RequireStudioPermission()
    {
        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/data/levels")).StatusCode);

        string playerUser = await web.CreateAccountAsync((ushort)Authority.Player);
        using var player = await web.LoginAsync(playerUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/data/levels")).StatusCode);

        string gmUser = await web.CreateAccountAsync((ushort)Authority.GameMaster);
        using var gm = await web.LoginAsync(gmUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.GetAsync("/api/data/levels")).StatusCode);

        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");

        var levels = await pm.GetFromJsonAsync<LevelsFileResponse>("/api/data/levels");
        Assert.NotNull(levels);
        Assert.NotEmpty(levels.Entries);
        Assert.NotEmpty(levels.Hash);

        var goals = await pm.GetFromJsonAsync<GoalsFileResponse>("/api/data/goals");
        Assert.NotNull(goals);
        Assert.NotEmpty(goals.Entries);

        var teleporters = await pm.GetFromJsonAsync<TeleportersFileResponse>("/api/data/teleporters");
        Assert.NotNull(teleporters);
        Assert.NotEmpty(teleporters.Entries);

        var talkers = await pm.GetFromJsonAsync<TalkersFileResponse>("/api/data/talkers");
        Assert.NotNull(talkers);
        Assert.NotEmpty(talkers.Entries);

        var mapInfo = await pm.GetFromJsonAsync<MapPickInfoResponse>("/api/data/maps/1002/info");
        Assert.NotNull(mapInfo);
        Assert.Equal(1002u, mapInfo.MapId);
    }

    [Fact]
    public async Task SaveEndpoints_RejectInvalidDataWithoutWriting()
    {
        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");

        var levels = await pm.GetFromJsonAsync<LevelsFileResponse>("/api/data/levels");
        Assert.NotNull(levels);
        var badLevels = new SaveLevelsRequest(levels.Hash, [
            new LevelEntryInput(1, 0, 2),
        ]);
        var levelsResp = await pm.PutAsJsonAsync("/api/data/levels", badLevels);
        Assert.Equal(HttpStatusCode.OK, levelsResp.StatusCode);
        var levelsResult = await levelsResp.Content.ReadFromJsonAsync<SaveDataResponse>();
        Assert.NotNull(levelsResult);
        Assert.False(levelsResult.Saved);
        Assert.NotEmpty(levelsResult.Problems);

        var talkers = await pm.GetFromJsonAsync<TalkersFileResponse>("/api/data/talkers");
        Assert.NotNull(talkers);
        var badTalkers = new SaveTalkersRequest(talkers.Hash, [
            new TalkerEntryInput(99999, 1, [], ""),
        ]);
        var talkersResp = await pm.PutAsJsonAsync("/api/data/talkers", badTalkers);
        Assert.Equal(HttpStatusCode.OK, talkersResp.StatusCode);
        var talkersResult = await talkersResp.Content.ReadFromJsonAsync<SaveDataResponse>();
        Assert.NotNull(talkersResult);
        Assert.False(talkersResult.Saved);
        Assert.NotEmpty(talkersResult.Problems);

        var teleporters = await pm.GetFromJsonAsync<TeleportersFileResponse>("/api/data/teleporters");
        Assert.NotNull(teleporters);
        var badTeleporters = new SaveTeleportersRequest(teleporters.Hash, [
            new TeleporterEntryInput(99999, 100, 1, ["Hi"], [], null),
        ]);
        var teleResp = await pm.PutAsJsonAsync("/api/data/teleporters", badTeleporters);
        Assert.Equal(HttpStatusCode.OK, teleResp.StatusCode);
        var teleResult = await teleResp.Content.ReadFromJsonAsync<SaveDataResponse>();
        Assert.NotNull(teleResult);
        Assert.False(teleResult.Saved);
        Assert.NotEmpty(teleResult.Problems);
    }

    [Fact]
    public async Task SaveEndpoints_ReturnConflictOnStaleHash()
    {
        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");

        var resp = await pm.PutAsJsonAsync("/api/data/levels", new SaveLevelsRequest("stale-hash", [
            new LevelEntryInput(1, 59, 2),
        ]));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
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

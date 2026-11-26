namespace Fetih.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Fetih.Game.States;
using Fetih.Web.Features.Changes;
using Xunit;

[Collection(WebCollection.Name)]
public class ChangesTests(WebFixture web)
{
    [Fact]
    public void ParseStatusPorcelain_MapsKnownStatuses()
    {
        string output = string.Join('\n',
            " M src/Fetih.Game/Data/npcs.json",
            "M  src/Fetih.Game/Data/maps.json",
            "?? src/Fetih.Game/Data/new.json",
            " D src/Fetih.Game/Data/old.json",
            "R  src/Fetih.Game/Data/a.json -> src/Fetih.Game/Data/b.json");

        var parsed = GitDataService.ParseStatusPorcelain(output);

        Assert.Equal("modified", parsed["npcs.json"]);
        Assert.Equal("modified", parsed["maps.json"]);
        Assert.Equal("untracked", parsed["new.json"]);
        Assert.Equal("deleted", parsed["old.json"]);
        Assert.Equal("renamed", parsed["b.json"]);
    }

    [Fact]
    public void CountDiffLines_IgnoresFileHeaders()
    {
        string diff = string.Join('\n',
            "--- a/npcs.json",
            "+++ b/npcs.json",
            "@@ -1,3 +1,3 @@",
            " line1",
            "-old line",
            "+new line 1",
            "+new line 2");

        var (added, removed) = GitDataService.CountDiffLines(diff);

        Assert.Equal(2, added);
        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task ChangesEndpoints_RequireStudioPermission()
    {
        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/changes")).StatusCode);

        string playerUser = await web.CreateAccountAsync((ushort)Authority.Player);
        using var player = await web.LoginAsync(playerUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/changes")).StatusCode);

        string gmUser = await web.CreateAccountAsync((ushort)Authority.GameMaster);
        using var gm = await web.LoginAsync(gmUser, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.GetAsync("/api/changes")).StatusCode);

        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");
        var response = await pm.GetAsync("/api/changes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<ChangesSummaryResponse>();
        Assert.NotNull(summary);
        Assert.Contains(summary.Files, f => f.File == "npcs.json" && f.Reloadable);
        Assert.Contains(summary.Files, f => f.File == "bosses.json" && !f.Reloadable);
    }

    [Fact]
    public async Task DiffAndReload_ValidateFileNamesAndReloadability()
    {
        string pmUser = await web.CreateAccountAsync((ushort)Authority.ProjectManager);
        using var pm = await web.LoginAsync(pmUser, "secret1");

        var diffOk = await pm.GetAsync("/api/changes/npcs.json/diff");
        Assert.Equal(HttpStatusCode.OK, diffOk.StatusCode);
        var diffBody = await diffOk.Content.ReadFromJsonAsync<FileDiffResponse>();
        Assert.NotNull(diffBody);
        Assert.Equal("npcs.json", diffBody.File);

        Assert.Equal(HttpStatusCode.NotFound, (await pm.GetAsync("/api/changes/evil.txt/diff")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pm.PostAsync("/api/changes/bosses.json/reload", null)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await pm.PostAsync("/api/changes/npcs.json/reload", null)).StatusCode);
    }
}

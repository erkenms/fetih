namespace Fetih.Web.Tests;

using Fetih.Web.Features.Changes;
using Xunit;

public class ChangesTests
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
    public void FormatApplyCommand_AvoidsDelimiterCollisionAndFormatsCommit()
    {
        string patch = "diff --git a/src/Fetih.Game/Data/npcs.json b/src/Fetih.Game/Data/npcs.json\n+FETIH_PATCH\n";
        string cmd = GitDataService.FormatApplyCommand(patch, ["npcs.json", "maps.json"]);

        Assert.Contains("git apply --3way <<'FETIH_PATCH_1'", cmd);
        Assert.Contains("\nFETIH_PATCH_1\ngit add src/Fetih.Game/Data && git commit -m \"Studio: npcs.json, maps.json\"", cmd);
    }
}

namespace Fetih.Web.Tests;

using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Fetih.Game.States;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[Collection(WebCollection.Name)]
public class StudioPageTests(WebFixture fixture)
{
    [Fact]
    public async Task SignedOutGoesToSignIn()
    {
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/studio/changes");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fstudio%2Fchanges", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task ViewerWithoutStudioSeesMonstersReadOnly()
    {
        var player = await fixture.SignInAsync(WebFixture.Player, WebFixture.PlayerPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/studio/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/studio/monsters")).StatusCode);

        string modEmail = await fixture.CreateAccountAsync((ushort)Authority.Moderator);
        var mod = await fixture.SignInAsync(modEmail, "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.GetAsync("/admin/studio/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.GetAsync("/admin/studio/levels")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.GetAsync("/admin/studio/monsters/new")).StatusCode);

        var list = await mod.GetAsync("/admin/studio/monsters");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        string listHtml = await list.Content.ReadAsStringAsync();
        Assert.Contains("Görüntüle", listHtml);
        Assert.DoesNotContain("Yeni canavar", listHtml);

        var detail = await mod.GetAsync("/admin/studio/monsters/1");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        string detailHtml = await detail.Content.ReadAsStringAsync();
        Assert.DoesNotContain("asp-page-handler=\"Delete\"", detailHtml);
        Assert.DoesNotContain(">Kaydet<", detailHtml);

        // Moderator cannot POST to monster save even with a valid anti-forgery token
        string token = await GetTokenAsync(mod, "/admin/studio/monsters/1");
        var save = await mod.PostAsync("/admin/studio/monsters/1?handler=Save", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Input.Id"] = "1",
                ["Input.Name"] = "Hacked",
            }));
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }

    [Fact]
    public async Task EditLevelShowsDiffOnChangesAndDiscardRevertsIt()
    {
        using var env = TempStudio.Create(initGit: true);
        using var factory = WithStudio(env.DataDir);
        string pmEmail = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await SignInAsync(factory, pmEmail, "secret1");

        string levelsHtml = await client.GetStringAsync("/admin/studio/levels");
        string hash = ExtractInputValue(levelsHtml, "Hash");

        var saved = await WebFixture.PostFormAsync(client, "/admin/studio/levels?handler=Save", new()
        {
            ["Hash"] = hash,
            ["Rows[0].Level"] = "1",
            ["Rows[0].Experience"] = "1234",
            ["Rows[0].UpLevTime"] = "2",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        string changesHtml = await client.GetStringAsync("/admin/studio/changes");
        Assert.Contains("levels.json", changesHtml);
        Assert.Contains("1234", changesHtml);
        Assert.Contains("diff-add", changesHtml);
        Assert.Contains("diff-del", changesHtml);

        var discarded = await WebFixture.PostFormAsync(client, "/admin/studio/changes?handler=Discard", new()
        {
            ["file"] = "levels.json",
        });
        Assert.Equal(HttpStatusCode.Redirect, discarded.StatusCode);

        string afterDiscard = await client.GetStringAsync("/admin/studio/changes");
        Assert.Contains("Değiştirilmiş veri dosyası yok.", afterDiscard);
        Assert.Contains("59", await File.ReadAllTextAsync(Path.Combine(env.DataDir, "levels.json")));
    }

    [Fact]
    public async Task BadRowSaveIsRefusedAndMarksBadCell()
    {
        using var env = TempStudio.Create();
        using var factory = WithStudio(env.DataDir);
        string pmEmail = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await SignInAsync(factory, pmEmail, "secret1");

        string before = await File.ReadAllTextAsync(Path.Combine(env.DataDir, "levels.json"));
        string levelsHtml = await client.GetStringAsync("/admin/studio/levels");
        string hash = ExtractInputValue(levelsHtml, "Hash");

        var response = await WebFixture.PostFormAsync(client, "/admin/studio/levels?handler=Save", new()
        {
            ["Hash"] = hash,
            ["Rows[0].Level"] = "1",
            ["Rows[0].Experience"] = "0",
            ["Rows[0].UpLevTime"] = "2",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Experience must be above 0", html);
        Assert.Contains("class=\"bad\"", html);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(env.DataDir, "levels.json")));
    }

    [Fact]
    public async Task TeleporterWithWallTargetIsRefused()
    {
        using var env = TempStudio.Create(withClientMap: true);
        using var factory = WithStudio(env.DataDir, clientDir: env.ClientDir);
        string pmEmail = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await SignInAsync(factory, pmEmail, "secret1");

        string before = await File.ReadAllTextAsync(Path.Combine(env.DataDir, "teleporters.json"));
        string html = await client.GetStringAsync("/admin/studio/teleporters");
        string hash = ExtractInputValue(html, "Hash");

        // x = 10 is a wall in TempStudio's 20x20 map 1002
        var response = await WebFixture.PostFormAsync(client, "/admin/studio/teleporters?handler=Save", new()
        {
            ["Hash"] = hash,
            ["Rows[0].Id"] = "100",
            ["Rows[0].Fare"] = "100",
            ["Rows[0].Face"] = "1",
            ["Rows[0].Text"] = "Where to?",
            ["Rows[0].Destinations[0].Name"] = "WallSpot",
            ["Rows[0].Destinations[0].Map"] = "1002",
            ["Rows[0].Destinations[0].X"] = "10",
            ["Rows[0].Destinations[0].Y"] = "5",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("yürünebilir değil", body);
        Assert.Contains("dest-row bad", body);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(env.DataDir, "teleporters.json")));
    }

    [Fact]
    public async Task MonsterCreateEditSearchAndDelete()
    {
        using var env = TempStudio.Create();
        using var factory = WithStudio(env.DataDir);
        string pmEmail = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await SignInAsync(factory, pmEmail, "secret1");

        string newHtml = await client.GetStringAsync("/admin/studio/monsters/new");
        string hash = ExtractInputValue(newHtml, "Hash");

        var created = await WebFixture.PostFormAsync(client, "/admin/studio/monsters/new?handler=Save", new()
        {
            ["Hash"] = hash,
            ["Input.Id"] = "500",
            ["Input.Name"] = "TestWolf",
            ["Input.Lookface"] = "101",
            ["Input.Level"] = "12",
            ["Input.Life"] = "250",
            ["Input.AttackMin"] = "10",
            ["Input.AttackMax"] = "20",
            ["Input.AttackRange"] = "1",
            ["Input.ViewRange"] = "8",
            ["Input.AttackSpeed"] = "1000",
            ["Input.MoveSpeed"] = "500",
            ["Input.AttackUser"] = "3",
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        string searchHtml = await client.GetStringAsync("/admin/studio/monsters?q=TestWolf&sort=level&dir=desc");
        Assert.Contains("TestWolf", searchHtml);

        string editHtml = await client.GetStringAsync("/admin/studio/monsters/500");
        string editHash = ExtractInputValue(editHtml, "Hash");
        var updated = await WebFixture.PostFormAsync(client, "/admin/studio/monsters/500?handler=Save", new()
        {
            ["Hash"] = editHash,
            ["Input.Id"] = "500",
            ["Input.Name"] = "TestWolf",
            ["Input.Lookface"] = "202",
            ["Input.Level"] = "25",
            ["Input.Life"] = "300",
            ["Input.AttackMin"] = "10",
            ["Input.AttackMax"] = "20",
            ["Input.AttackRange"] = "1",
            ["Input.ViewRange"] = "8",
            ["Input.AttackSpeed"] = "1000",
            ["Input.MoveSpeed"] = "500",
            ["Input.AttackUser"] = "3",
        });
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);

        string afterEdit = await client.GetStringAsync("/admin/studio/monsters/500");
        Assert.Contains("value=\"25\"", afterEdit);
        Assert.Contains("value=\"202\"", afterEdit);

        string delHash = ExtractInputValue(afterEdit, "Hash");
        var deleted = await WebFixture.PostFormAsync(client, "/admin/studio/monsters/500?handler=Delete", new()
        {
            ["Hash"] = delHash,
        });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/studio/monsters/500")).StatusCode);
    }

    [Fact]
    public async Task StudioReadOnlyRefusesAllWrites()
    {
        using var env = TempStudio.Create();
        using var factory = WithStudio(env.DataDir, readOnly: true);
        string pmEmail = await fixture.CreateAccountAsync((ushort)Authority.ProjectManager);
        var client = await SignInAsync(factory, pmEmail, "secret1");

        string changesHtml = await client.GetStringAsync("/admin/studio/changes");
        Assert.Contains("salt okunur", changesHtml);

        string levelsHtml = await client.GetStringAsync("/admin/studio/levels");
        Assert.Contains("salt okunur", levelsHtml);
        string hash = ExtractInputValue(levelsHtml, "Hash");

        var saveLevels = await WebFixture.PostFormAsync(client, "/admin/studio/levels?handler=Save", new()
        {
            ["Hash"] = hash,
            ["Rows[0].Level"] = "1",
            ["Rows[0].Experience"] = "999",
            ["Rows[0].UpLevTime"] = "2",
        });
        Assert.Equal(HttpStatusCode.Forbidden, saveLevels.StatusCode);

        string token = await GetTokenAsync(client, "/admin/studio/changes");
        var discard = await client.PostAsync("/admin/studio/changes?handler=Discard", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["file"] = "levels.json",
            }));
        Assert.Equal(HttpStatusCode.Forbidden, discard.StatusCode);

        var saveMonster = await client.PostAsync("/admin/studio/monsters/1?handler=Save", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Input.Id"] = "1",
                ["Input.Name"] = "Pheasant",
                ["Input.Life"] = "99",
            }));
        Assert.Equal(HttpStatusCode.Forbidden, saveMonster.StatusCode);
    }

    private WebApplicationFactory<Program> WithStudio(string dataDir, bool readOnly = false, string clientDir = null) =>
        fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            var cfg = (WebConfiguration)services.Single(d => d.ServiceType == typeof(WebConfiguration)).ImplementationInstance!;
            cfg.Paths.DataDirectory = dataDir;
            cfg.Web.StudioReadOnly = readOnly;
            if (clientDir != null)
                cfg.Paths.ClientDirectory = clientDir;
        }));

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await WebFixture.PostFormAsync(client, "/giris", new() { ["kullanici"] = username, ["sifre"] = password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        string page = await client.GetStringAsync(url);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        if (!match.Success)
        {
            page = await client.GetStringAsync("/giris");
            match = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        }
        Assert.True(match.Success, "No anti-forgery token found");
        return match.Groups[1].Value;
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\" value=\"([^\"]*)\"");
        Assert.True(match.Success, $"Input '{name}' not found in HTML");
        return match.Groups[1].Value;
    }

    private sealed class TempStudio : IDisposable
    {
        public string Root { get; }
        public string DataDir { get; }
        public string ClientDir { get; }

        private TempStudio(string root)
        {
            this.Root = root;
            this.DataDir = Path.Combine(root, "data");
            this.ClientDir = Path.Combine(root, "client");
        }

        public static TempStudio Create(bool initGit = false, bool withClientMap = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "fetih-studio-page-test-" + Guid.NewGuid().ToString("N")[..8]);
            var env = new TempStudio(root);
            Directory.CreateDirectory(env.DataDir);

            File.WriteAllText(Path.Combine(env.DataDir, "levels.json"), """
                { "levels": [
                  { "level": 1, "experience": 59, "upLevTime": 2 }
                ] }
                """);
            File.WriteAllText(Path.Combine(env.DataDir, "goals.json"), """
                { "npcs": [
                  { "id": 100011, "lookface": 427, "role": 21, "level": 20, "life": 10000, "map": 1002, "x": 5, "y": 5 }
                ] }
                """);
            File.WriteAllText(Path.Combine(env.DataDir, "npcs.json"), """
                { "npcs": [
                  { "id": 100, "lookface": 280, "role": 2, "map": 1002, "x": 5, "y": 5 }
                ] }
                """);
            File.WriteAllText(Path.Combine(env.DataDir, "teleporters.json"), """
                { "teleporters": [
                  { "id": 100, "fare": 100, "face": 1, "text": ["Where to?"], "destinations": [{ "name": "Town", "map": 1002, "x": 5, "y": 5 }] }
                ] }
                """);
            File.WriteAllText(Path.Combine(env.DataDir, "talkers.json"), """
                { "talkers": [
                  { "id": 100, "face": 1, "text": ["Hello"], "reply": "Hi" }
                ] }
                """);
            File.WriteAllText(Path.Combine(env.DataDir, "monsters.json"), """
                { "monsters": [
                  { "id": 1, "name": "Pheasant", "lookface": 100, "level": 1, "life": 33, "attackMin": 5, "attackMax": 6 }
                ] }
                """);

            if (withClientMap)
            {
                Directory.CreateDirectory(Path.Combine(env.ClientDir, "ini"));
                Directory.CreateDirectory(Path.Combine(env.ClientDir, "map"));
                File.WriteAllBytes(Path.Combine(env.ClientDir, "ini", "GameMap.dat"),
                    MapsTests.BuildGameMapDat([(1002, "map/test.dmap", 256)]));
                File.WriteAllBytes(Path.Combine(env.ClientDir, "map", "test.dmap"),
                    MapsTests.BuildDMap(20, 20, (x, y) => x != 10, (_, _) => 0, [(2, 2, 0)]));
            }

            if (initGit)
            {
                RunGit(env.DataDir, "init");
                RunGit(env.DataDir, "config", "user.email", "test@test.local");
                RunGit(env.DataDir, "config", "user.name", "Test");
                RunGit(env.DataDir, "add", ".");
                RunGit(env.DataDir, "commit", "-m", "init");
            }

            return env;
        }

        private static void RunGit(string dir, params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
        }

        public void Dispose()
        {
            try { Directory.Delete(this.Root, recursive: true); } catch { }
        }
    }
}

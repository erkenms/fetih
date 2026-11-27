using System.Net;
using System.Text;
using Fetih.Patch;
using Xunit;

namespace Fetih.Patch.Tests;

public sealed class SyncTests : IDisposable
{
    readonly string _temp = Path.Combine(Path.GetTempPath(), "fetih-sync-tests-" + Guid.NewGuid().ToString("N"));
    readonly string _client;
    readonly string _install;
    readonly FakeServer _server;

    public SyncTests()
    {
        _client = Path.Combine(_temp, "client");
        _install = Path.Combine(_temp, "install");
        Directory.CreateDirectory(_client);
        Directory.CreateDirectory(_install);
        _server = new FakeServer(Path.Combine(_temp, "patch"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
            Directory.Delete(_temp, recursive: true);
    }

    void Client(string path, string text)
    {
        var full = Path.Combine(_client, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    string Installed(string path) => File.ReadAllText(Path.Combine(_install, path));

    PatchManifest Publish(string channel = "prod") => PatchPublisher.Publish(new PatchPublishOptions
    {
        ClientDirectory = _client,
        PatchDirectory = Path.Combine(_temp, "patch", channel),
    }).Manifest;

    async Task<(LocalState State, SyncPlan Plan, PatchSync Sync)> Sync(LocalState state, string channel = "prod", bool repair = false)
    {
        var http = new HttpClient(_server);
        var baseUri = new Uri($"http://fetih.test/{channel}/");
        var manifest = await PatchSync.FetchManifestAsync(http, baseUri, CancellationToken.None);
        var plan = SyncPlan.Create(_install, manifest, state, repair);
        var sync = new PatchSync(http, baseUri, _install) { RetryDelay = TimeSpan.Zero };
        return (await sync.ApplyAsync(manifest, plan, state, CancellationToken.None), plan, sync);
    }

    [Fact]
    public async Task FirstSyncDownloadsEverythingAndASecondStartHashesNothing()
    {
        Client("Conquer.exe", "exe");
        Client("ini/Shop.dat", new string('s', 5000));
        Client("ini/GameSetUp.ini", "[ScreenMode]\r\n");
        Client("empty.txt", "");
        Publish();

        var (state, plan, sync) = await Sync(new LocalState());
        Assert.Equal(4, plan.Download.Count);
        Assert.Equal(plan.DownloadBytes, sync.DoneBytes);
        Assert.Equal("exe", Installed("Conquer.exe"));
        Assert.Equal(3, state.Files.Count);
        Assert.Equal("", Installed("empty.txt"));
        Assert.NotNull(state.Version);

        var again = SyncPlan.Create(_install, PatchManifestJson.LoadFile(Path.Combine(_temp, "patch", "prod", "manifest.json")), LocalState.Load(_install));
        Assert.False(again.HasWork);
        Assert.Equal(0, again.Hashed);
    }

    [Fact]
    public async Task OneChangedFileIsTheOnlyDownloadAndRemovedFilesGo()
    {
        Client("Conquer.exe", "exe");
        Client("a.dat", "a1");
        Client("old.dat", "old");
        Publish();
        var (state, _, _) = await Sync(new LocalState());
        File.WriteAllText(Path.Combine(_install, "mine.txt"), "not ours");

        Client("a.dat", "a2");
        File.Delete(Path.Combine(_client, "old.dat"));
        Publish();
        var (_, plan, _) = await Sync(state);

        Assert.Equal(["a.dat"], plan.Download.Select(e => e.Path));
        Assert.Equal(["old.dat"], plan.Delete);
        Assert.Equal("a2", Installed("a.dat"));
        Assert.False(File.Exists(Path.Combine(_install, "old.dat")));
        Assert.True(File.Exists(Path.Combine(_install, "mine.txt")));
    }

    [Fact]
    public async Task PlayerOwnedFilesAreKeptAndFetchedOnlyWhenMissing()
    {
        Client("ini/GameSetUp.ini", "server");
        Client("Log/0/Setup.ini", "server");
        Publish();
        var (state, _, _) = await Sync(new LocalState());
        File.WriteAllText(Path.Combine(_install, "ini/GameSetUp.ini"), "player");
        File.Delete(Path.Combine(_install, "Log/0/Setup.ini"));

        Client("ini/GameSetUp.ini", "server2");
        Publish();
        await Sync(state);

        Assert.Equal("player", Installed("ini/GameSetUp.ini"));
        Assert.Equal("server", Installed("Log/0/Setup.ini"));
    }

    [Fact]
    public async Task AFileChangedOnDiskIsHashedAndReplacedAndRepairChecksAll()
    {
        Client("a.dat", "aaaa");
        Client("b.dat", "bbbb");
        Publish();
        var (state, _, _) = await Sync(new LocalState());

        File.WriteAllText(Path.Combine(_install, "a.dat"), "xxxx");
        var (state2, plan, _) = await Sync(state);
        Assert.Equal(1, plan.Hashed);
        Assert.Equal(["a.dat"], plan.Download.Select(e => e.Path));
        Assert.Equal("aaaa", Installed("a.dat"));

        var (_, repair, _) = await Sync(state2, repair: true);
        Assert.Equal(2, repair.Hashed);
        Assert.False(repair.HasWork);
    }

    [Fact]
    public async Task ChannelSwitchGoesBothWaysAndKeepsPlayerFiles()
    {
        Client("Conquer.exe", "prod");
        Client("ini/GameSetUp.ini", "defaults");
        Publish("prod");
        Client("Conquer.exe", "test");
        Client("test-only.dat", "t");
        Publish("test");

        var (state, _, _) = await Sync(new LocalState(), "prod");
        File.WriteAllText(Path.Combine(_install, "ini/GameSetUp.ini"), "player");

        (state, _, _) = await Sync(state, "test");
        Assert.Equal("test", Installed("Conquer.exe"));
        Assert.Equal("t", Installed("test-only.dat"));

        (state, _, _) = await Sync(state, "prod");
        Assert.Equal("prod", Installed("Conquer.exe"));
        Assert.False(File.Exists(Path.Combine(_install, "test-only.dat")));
        Assert.Equal("player", Installed("ini/GameSetUp.ini"));
    }

    [Fact]
    public async Task DownloadsResumeAfterADroppedConnectionAndRetryAfterErrors()
    {
        var big = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(100_000));
        Client("big.dat", big);
        Client("small.dat", "s");
        Publish();
        _server.CutAfter = 50_000;
        _server.FailFirst = 1;

        await Sync(new LocalState());

        Assert.Equal(big, Installed("big.dat"));
        Assert.Contains(_server.Ranges, r => r > 0);
    }

    [Fact]
    public async Task AnInterruptedSyncStillKnowsTheOldManifestsFiles()
    {
        Client("a.dat", "a");
        Client("old.dat", "old");
        Publish();
        var (state, _, _) = await Sync(new LocalState());

        File.Delete(Path.Combine(_client, "old.dat"));
        Client("a.dat", "a2");
        Publish();
        _server.FailAlways = true;
        await Assert.ThrowsAnyAsync<Exception>(() => Sync(state));
        _server.FailAlways = false;

        var (_, plan, _) = await Sync(LocalState.Load(_install));
        Assert.Equal(["old.dat"], plan.Delete);
    }

    [Fact]
    public void ManifestPathsCantLeaveTheFolder()
    {
        Assert.Throws<InvalidDataException>(() => SyncPlan.FullPath(_install, "../evil.exe"));
        Assert.Throws<InvalidDataException>(() => SyncPlan.FullPath(_install, "C:/Windows/evil.exe"));
        Assert.Throws<InvalidDataException>(() => SyncPlan.FullPath(_install, "a//b"));
        Assert.EndsWith("ini" + Path.DirectorySeparatorChar + "Shop.dat", SyncPlan.FullPath(_install, @"ini\Shop.dat"));
    }

    [Fact]
    public void LauncherIsCurrentByStateOrHash()
    {
        Client("Fetih.exe", "launcher-v1");
        var manifest = Publish();
        var exe = Path.Combine(_client, "Fetih.exe");

        Assert.True(SyncPlan.LauncherIsCurrent(exe, manifest, new LocalState(), out var local));
        Assert.True(SyncPlan.LauncherIsCurrent(exe, manifest, new LocalState { Launcher = local }, out _));

        Client("Fetih.exe", "launcher-v2");
        Assert.False(SyncPlan.LauncherIsCurrent(exe, manifest, new LocalState { Launcher = local }, out _));
    }

    // Serves <root>/<channel>/... like Caddy: Range requests, and failures on demand
    sealed class FakeServer(string root) : HttpMessageHandler
    {
        public long CutAfter { get; set; } = long.MaxValue;
        public int FailFirst { get; set; }
        public bool FailAlways { get; set; }
        public List<long> Ranges { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = Path.Combine(root, request.RequestUri!.AbsolutePath.TrimStart('/'));
            var isObject = request.RequestUri.AbsolutePath.Contains("/objects/");
            if (isObject && (FailAlways || FailFirst-- > 0))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            if (!File.Exists(path))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var bytes = File.ReadAllBytes(path);
            var from = request.Headers.Range?.Ranges.First().From ?? 0;
            if (isObject)
                lock (Ranges)
                    Ranges.Add(from);
            var body = bytes[(int)from..];
            if (isObject && body.Length > CutAfter)
            {
                body = body[..(int)CutAfter];
                CutAfter = long.MaxValue;
            }
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
            return Task.FromResult(response);
        }
    }
}

public sealed class GameSetUpTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "fetih-ini-" + Guid.NewGuid().ToString("N") + ".ini");

    public void Dispose() => File.Delete(_path);

    const string Base5619 =
        "[ScreenMode]\r\nScreenModeRecord=2\r\nScrWidth=1024\r\nScrHeight=768\r\nFullScrType=1\r\n[Group]\r\nGroupRecord=0\r\n" +
        "[GameMode]\r\nLowTimes=0\r\nGameModeRecord=0\r\nHighTimes=0\r\nUserDataVer=1\r\nNormalTimes=1\r\n";

    [Fact]
    public void HighQualityIsWhatPlayExeWrote()
    {
        File.WriteAllText(_path, Base5619);
        GameSetUp.SetHighQuality(_path);
        Assert.Equal(Base5619.Replace("GameModeRecord=0", "GameModeRecord=1").Replace("HighTimes=0", "HighTimes=1"), File.ReadAllText(_path));
    }

    [Fact]
    public void ScreenTouchesOnlyItsKeys()
    {
        File.WriteAllText(_path, Base5619);
        GameSetUp.WriteScreen(_path, new ScreenMode(1707, 960, FullScreen: true));
        Assert.Equal(
            Base5619.Replace("ScreenModeRecord=2", "ScreenModeRecord=4").Replace("ScrWidth=1024", "ScrWidth=1707").Replace("ScrHeight=768", "ScrHeight=960"),
            File.ReadAllText(_path));
        Assert.Equal(new ScreenMode(1707, 960, true), GameSetUp.ReadScreen(_path));
    }

    [Fact]
    public void MissingKeysGoIntoTheirSection()
    {
        File.WriteAllText(_path, "[ScreenMode]\nScrWidth=800\n\n[Group]\nGroupRecord=3\n");
        GameSetUp.WriteScreen(_path, new ScreenMode(1280, 720, FullScreen: false));
        Assert.Equal(
            "[ScreenMode]\nScrWidth=1280\nScreenModeRecord=4\nScrHeight=720\nFullScrType=0\n\n[Group]\nGroupRecord=3\n",
            File.ReadAllText(_path));
    }

    [Fact]
    public void ReadsTheClientsOwnRecords()
    {
        File.WriteAllText(_path, "[ScreenMode]\r\nScreenModeRecord=1\r\n");
        Assert.Equal(new ScreenMode(800, 600, false), GameSetUp.ReadScreen(_path));
    }

    [Fact]
    public void SizesStartWithTheScreen()
    {
        Assert.Equal((1707, 960), GameSetUp.Sizes(1707, 960)[0]);
        var sizes = GameSetUp.Sizes(1366, 768);
        Assert.Equal((1366, 768), sizes[0]);
        Assert.Equal(1, sizes.Count(s => s == (1366, 768)));
        Assert.DoesNotContain((1920, 1080), sizes);
        Assert.Contains((800, 600), sizes);
    }
}

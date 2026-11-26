using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Fetih.Patch;
using Xunit;

namespace Fetih.Patch.Tests;

public sealed class PatchManifestDiffTests
{
    static PatchFileEntry Entry(string path, string hash, long size = 100, bool playerOwned = false) => new()
    {
        Path = path,
        Size = size,
        Sha256 = hash,
        CompressedSize = size,
        Compression = PatchCompression.None,
        PlayerOwned = playerOwned,
    };

    static PatchManifest Manifest(string version, PatchFileEntry[] files, PatchFileEntry? launcher = null, string minLauncher = "1.0.0") => new()
    {
        Version = version,
        BuiltAt = DateTimeOffset.UnixEpoch,
        MinLauncherVersion = minLauncher,
        Launcher = launcher,
        Files = files,
    };

    [Fact]
    public void FreshManifestMarksEverythingAdded()
    {
        var next = Manifest("v1", [Entry("Conquer.exe", "aaa"), Entry("ini/Shop.dat", "bbb")], launcher: Entry("Fetih.exe", "ccc"));
        var diff = PatchManifestDiff.Compute(null, next);

        Assert.True(diff.HasChanges);
        Assert.Equal(2, diff.Added.Count);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Removed);
        Assert.Equal(0, diff.UnchangedCount);
        Assert.True(diff.LauncherChanged);
    }

    [Fact]
    public void DiffIsBackslashAndCaseInsensitive()
    {
        var prev = Manifest("v1", [Entry(@"ini\GameSetUp.ini", "aaa", playerOwned: true), Entry("Conquer.exe", "bbb")]);
        var next = Manifest("v2", [Entry("ini/gamesetup.ini", "aaa", playerOwned: true), Entry("conquer.exe", "bbb")]);

        var diff = PatchManifestDiff.Compute(prev, next);

        Assert.False(diff.HasChanges);
        Assert.Equal(2, diff.UnchangedCount);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void DiffDetectsAddedChangedRemovedAndLauncherUpdates()
    {
        var prev = Manifest(
            "v1",
            [Entry("Conquer.exe", "aaa"), Entry("ini/a.ini", "bbb"), Entry("old.dat", "ccc")],
            launcher: Entry("Fetih.exe", "l1"),
            minLauncher: "1.0.0");

        var next = Manifest(
            "v2",
            [Entry("Conquer.exe", "aaa"), Entry("ini/a.ini", "bbb2"), Entry("new.dat", "ddd")],
            launcher: Entry("Fetih.exe", "l2"),
            minLauncher: "1.1.0");

        var diff = PatchManifestDiff.Compute(prev, next);

        Assert.True(diff.HasChanges);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Single(diff.Added, e => e.Path == "new.dat");
        Assert.Single(diff.Changed, e => e.Path == "ini/a.ini");
        Assert.Single(diff.Removed, e => e.Path == "old.dat");
        Assert.True(diff.LauncherChanged);
        Assert.True(diff.MinLauncherVersionChanged);
    }
}

public sealed class PlayerOwnedFilesTests
{
    [Theory]
    [InlineData("ini/GameSetUp.ini", true)]
    [InlineData(@"INI\gamesetup.ini", true)]
    [InlineData("Log/0/Setup.ini", true)]
    [InlineData("LOG/Altay/Hero/Setup.ini", true)]
    [InlineData("LOG/Altay/Hero/CHAT/2026.txt", true)]
    [InlineData("debug/2026_11_23.log", true)]
    [InlineData("debug/2026_11_23__12_00_00.dmp", true)]
    [InlineData("screenshot/12345.jpg", true)]
    [InlineData("AutoPatch/Update.log", true)]
    [InlineData("AutoPatch/update/enzf/state.dat", true)]
    [InlineData("p2pTemp1.log", true)]
    [InlineData("debug.log", true)]
    [InlineData("ini/c3.wdb", true)]
    [InlineData("Conquer.exe", false)]
    [InlineData("Server.dat", false)]
    [InlineData("ini/itemtype.dat", false)]
    [InlineData("ini/StrRes.ini", false)]
    [InlineData("c3.wdf", false)]
    [InlineData("AutoPatch/GUI.ini", false)]
    public void ClassifiesPlayerOwnedPaths(string path, bool expected)
    {
        Assert.Equal(expected, PlayerOwnedFiles.IsPlayerOwned(path));
    }
}

public sealed class PatchPublisherTests : IDisposable
{
    readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "fetih-patch-tests-" + Guid.NewGuid().ToString("N"));
    readonly string _clientDir;
    readonly string _patchDir;

    public PatchPublisherTests()
    {
        _clientDir = Path.Combine(_tempRoot, "client");
        _patchDir = Path.Combine(_tempRoot, "patch");
        Directory.CreateDirectory(_clientDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    void WriteClientFile(string relPath, byte[] bytes)
    {
        var full = Path.Combine(_clientDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    void WriteClientText(string relPath, string content) =>
        WriteClientFile(relPath, Encoding.UTF8.GetBytes(content));

    [Fact]
    public void PublishingTwiceWithOneChangedIniAddsOneObjectAndOneManifest()
    {
        WriteClientText("Conquer.exe", new string('A', 4096));
        WriteClientText("Fetih.exe", new string('L', 2048));
        WriteClientText("ini/StrRes.ini", "hello=world\n" + new string('x', 1024));
        WriteClientText("ini/GameSetUp.ini", "[ScreenMode]\r\nScrWidth=1024\r\n");

        var t1 = new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);
        var r1 = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t1,
            GitShortSha = "aaa1111",
        });

        Assert.True(r1.PublishedNewManifest);
        Assert.Equal(4, r1.ObjectsWritten);
        Assert.Equal(0, r1.ObjectsReused);
        Assert.NotNull(r1.Manifest.Launcher);
        Assert.Equal("Fetih.exe", r1.Manifest.Launcher!.Path);
        Assert.Equal(3, r1.Manifest.Files.Count);
        Assert.True(r1.Manifest.Files.Single(f => f.Path == "ini/GameSetUp.ini").PlayerOwned);
        Assert.False(r1.Manifest.Files.Single(f => f.Path == "ini/StrRes.ini").PlayerOwned);

        var objectsBefore = Directory.GetFiles(Path.Combine(_patchDir, "objects")).Length;
        var manifestsBefore = Directory.GetFiles(Path.Combine(_patchDir, "manifests")).Length;
        Assert.Equal(4, objectsBefore);
        Assert.Equal(1, manifestsBefore);

        // Publishing again with no changes adds nothing
        var rSame = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t1.AddMinutes(1),
            GitShortSha = "aaa1111",
        });
        Assert.False(rSame.PublishedNewManifest);
        Assert.Equal(r1.Manifest.Version, rSame.Manifest.Version);
        Assert.Equal(0, rSame.ObjectsWritten);
        Assert.Equal(4, rSame.ObjectsReused);

        // Change one ini and publish again
        WriteClientText("ini/StrRes.ini", "hello=fetih\n" + new string('x', 1024));
        var r2 = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t1.AddMinutes(5),
            GitShortSha = "bbb2222",
        });

        Assert.True(r2.PublishedNewManifest);
        Assert.Equal(1, r2.ObjectsWritten);
        Assert.Equal(3, r2.ObjectsReused);
        Assert.Single(r2.Diff.Changed, e => e.Path == "ini/StrRes.ini");
        Assert.Empty(r2.Diff.Added);
        Assert.Empty(r2.Diff.Removed);

        Assert.Equal(objectsBefore + 1, Directory.GetFiles(Path.Combine(_patchDir, "objects")).Length);
        Assert.Equal(manifestsBefore + 1, Directory.GetFiles(Path.Combine(_patchDir, "manifests")).Length);

        var latest = PatchManifestJson.LoadFile(Path.Combine(_patchDir, "manifest.json"));
        Assert.Equal(r2.Manifest.Version, latest.Version);
    }

    [Fact]
    public void CompressionFallsBackToRawWhenNotSmallerAndRoundTrips()
    {
        var compressible = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ConquerOnline5619\n", 200)));
        var incompressible = RandomNumberGenerator.GetBytes(2048);
        var empty = Array.Empty<byte>();

        WriteClientFile("ini/text.ini", compressible);
        WriteClientFile("data/random.bin", incompressible);
        WriteClientFile("Conquer.map", empty);

        var result = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            GitShortSha = "abc1234",
        });

        var textEntry = result.Manifest.Files.Single(f => f.Path == "ini/text.ini");
        var randEntry = result.Manifest.Files.Single(f => f.Path == "data/random.bin");
        var emptyEntry = result.Manifest.Files.Single(f => f.Path == "Conquer.map");

        Assert.Equal(PatchCompression.Gzip, textEntry.Compression);
        Assert.True(textEntry.CompressedSize < textEntry.Size);

        Assert.Equal(PatchCompression.None, randEntry.Compression);
        Assert.Equal(randEntry.Size, randEntry.CompressedSize);

        Assert.Equal(PatchCompression.None, emptyEntry.Compression);
        Assert.Equal(0, emptyEntry.CompressedSize);

        // Verify all objects decompress and hash-match
        var restoreDir = Path.Combine(_tempRoot, "restored");
        foreach (var entry in result.Manifest.Files)
        {
            var objPath = Path.Combine(_patchDir, "objects", entry.Sha256);
            var destPath = Path.Combine(restoreDir, entry.Path);
            PatchObjectStore.DecompressAndVerify(objPath, entry, destPath);
        }

        Assert.Equal(compressible, File.ReadAllBytes(Path.Combine(restoreDir, "ini/text.ini")));
        Assert.Equal(incompressible, File.ReadAllBytes(Path.Combine(restoreDir, "data/random.bin")));
        Assert.Equal(empty, File.ReadAllBytes(Path.Combine(restoreDir, "Conquer.map")));
    }

    [Fact]
    public void GarbageCollectionRemovesObjectsUnreferencedByLastNVersions()
    {
        WriteClientText("shared.dat", "stays the same");
        var t = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        var hashes = new List<string>();
        for (var i = 1; i <= 4; i++)
        {
            WriteClientText("changing.ini", $"version-{i}-" + new string('z', 512));
            var res = PatchPublisher.Publish(new PatchPublishOptions
            {
                ClientDirectory = _clientDir,
                PatchDirectory = _patchDir,
                BuiltAt = t.AddMinutes(i),
                GitShortSha = $"sha000{i}",
                KeepVersions = 2,
            });
            hashes.Add(res.Manifest.Files.Single(f => f.Path == "changing.ini").Sha256);
        }

        // Only the last 2 manifests remain
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_patchDir, "manifests"), "*.json").Length);

        // Objects from v1 and v2 were collected; v3, v4, and shared.dat remain
        var objectsDir = Path.Combine(_patchDir, "objects");
        Assert.False(File.Exists(Path.Combine(objectsDir, hashes[0])));
        Assert.False(File.Exists(Path.Combine(objectsDir, hashes[1])));
        Assert.True(File.Exists(Path.Combine(objectsDir, hashes[2])));
        Assert.True(File.Exists(Path.Combine(objectsDir, hashes[3])));
        Assert.Equal(3, Directory.GetFiles(objectsDir).Length);
    }

    [Fact]
    public void ZipUnpacksIdenticallyToBuildOutputAndUpdatesOnlyWhenNeeded()
    {
        WriteClientText("Conquer.exe", "exe-bytes");
        WriteClientText("Fetih.exe", "launcher-bytes");
        WriteClientText("ini/GameSetUp.ini", "[ScreenMode]\r\nScrWidth=1024\r\n");
        WriteClientText("Log/0/Setup.ini", "[Record]\r\nMusicRecord=50\r\n");
        Directory.CreateDirectory(Path.Combine(_clientDir, "screenshot"));

        var t = new DateTimeOffset(2026, 11, 26, 14, 0, 0, TimeSpan.Zero);

        // Without --zip on a fresh patch dir, no zip is created
        var rNoZip = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t,
            GitShortSha = "v1sha",
            BuildZip = false,
        });
        Assert.False(rNoZip.ZipBuilt);
        Assert.Null(rNoZip.ZipPath);

        // With --zip, creates versioned zip + relative fetih.zip symlink
        var rZip = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t.AddMinutes(1),
            GitShortSha = "v1sha",
            BuildZip = true,
        });
        Assert.True(rZip.ZipBuilt);
        Assert.NotNull(rZip.ZipPath);
        Assert.True(File.Exists(rZip.ZipPath));

        var stableLink = Path.Combine(_patchDir, "download", "fetih.zip");
        var target = File.ResolveLinkTarget(stableLink, returnFinalTarget: false);
        Assert.NotNull(target);
        Assert.Equal(Path.GetFileName(rZip.ZipPath), target!.Name);

        // Unpack and verify exact match with build output (files + empty screenshot dir)
        var unpackDir = Path.Combine(_tempRoot, "unpacked");
        ZipFile.ExtractToDirectory(stableLink, unpackDir);

        var expectedFiles = Directory.EnumerateFiles(_clientDir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(_clientDir, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var actualFiles = Directory.EnumerateFiles(unpackDir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(unpackDir, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expectedFiles, actualFiles);
        foreach (var rel in expectedFiles)
            Assert.Equal(File.ReadAllBytes(Path.Combine(_clientDir, rel)), File.ReadAllBytes(Path.Combine(unpackDir, rel)));
        Assert.True(Directory.Exists(Path.Combine(unpackDir, "screenshot")));

        // Publishing again with --zip and no changes skips rebuilding the zip
        var rZipAgain = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t.AddMinutes(2),
            GitShortSha = "v1sha",
            BuildZip = true,
        });
        Assert.False(rZipAgain.ZipBuilt);

        // Changing a file when fetih.zip already exists updates the zip even without --zip
        WriteClientText("ini/StrRes.ini", "updated");
        var rChanged = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = _clientDir,
            PatchDirectory = _patchDir,
            BuiltAt = t.AddMinutes(3),
            GitShortSha = "v2sha",
            BuildZip = false,
        });
        Assert.True(rChanged.ZipBuilt);
        Assert.NotEqual(rZip.ZipPath, rChanged.ZipPath);
        Assert.Equal(Path.GetFileName(rChanged.ZipPath), File.ResolveLinkTarget(stableLink, false)!.Name);
    }
}

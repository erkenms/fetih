using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Fetih.Patch;

public sealed record PatchPublishOptions
{
    public required string ClientDirectory { get; init; }
    public required string PatchDirectory { get; init; }
    public string? Version { get; init; }
    public string GitShortSha { get; init; } = "nogit";
    public DateTimeOffset? BuiltAt { get; init; }
    public string LauncherFileName { get; init; } = "Fetih.exe";
    public string StartFileName { get; init; } = "Conquer.exe";
    public int KeepVersions { get; init; } = 10;
    public int MaxDegreeOfParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 8);
}

public sealed record PatchPublishResult
{
    public required PatchManifest Manifest { get; init; }
    public required PatchManifestDiff Diff { get; init; }
    public required bool PublishedNewManifest { get; init; }
    public required int ObjectsWritten { get; init; }
    public required int ObjectsReused { get; init; }
    public required int ObjectsGarbageCollected { get; init; }
    public required int ManifestsGarbageCollected { get; init; }

    // download/Fetih.exe, what /indir hands out; null when the client has no launcher
    public string? LauncherDownloadPath { get; init; }
}

public static class PatchObjectStore
{
    public static Stream OpenDecompressStream(Stream source, string compression) => compression switch
    {
        PatchCompression.None => source,
        PatchCompression.Gzip => new GZipStream(source, CompressionMode.Decompress, leaveOpen: false),
        PatchCompression.Brotli => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: false),
        _ => throw new InvalidDataException($"Unknown compression '{compression}'.")
    };

    public static void DecompressAndVerify(string objectPath, PatchFileEntry entry, string destinationPath)
    {
        using var raw = File.OpenRead(objectPath);
        DecompressAndVerify(raw, entry, destinationPath);
    }

    // Writes a temp file next to the destination, checks size and SHA-256, then renames it
    public static void DecompressAndVerify(Stream raw, PatchFileEntry entry, string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            long written = 0;
            string actualHash;
            using (var input = OpenDecompressStream(raw, entry.Compression))
            using (var output = File.Create(tempPath))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hasher.AppendData(buffer, 0, read);
                    written += read;
                }
                actualHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
            }

            if (written != entry.Size)
                throw new InvalidDataException($"Size mismatch for {entry.Path}: expected {entry.Size}, got {written}.");
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 mismatch for {entry.Path}: expected {entry.Sha256}, got {actualHash}.");

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of temp files
        }
    }
}

public static class PatchPublisher
{
    public static PatchPublishResult Publish(PatchPublishOptions options)
    {
        if (options.KeepVersions < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "KeepVersions must be at least 1.");

        var clientDir = Path.GetFullPath(options.ClientDirectory);
        if (!Directory.Exists(clientDir))
            throw new DirectoryNotFoundException($"Client directory not found: {clientDir}");

        var patchDir = Path.GetFullPath(options.PatchDirectory);
        var objectsDir = Path.Combine(patchDir, "objects");
        var manifestsDir = Path.Combine(patchDir, "manifests");
        var downloadDir = Path.Combine(patchDir, "download");
        Directory.CreateDirectory(objectsDir);
        Directory.CreateDirectory(manifestsDir);

        var latestManifestPath = Path.Combine(patchDir, "manifest.json");
        var previousManifest = File.Exists(latestManifestPath)
            ? PatchManifestJson.LoadFile(latestManifestPath)
            : null;

        // Build lookup of known objects from existing manifests so unchanged objects aren't re-compressed
        var knownObjects = LoadKnownObjects(manifestsDir, previousManifest);

        var builtAt = (options.BuiltAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var version = options.Version ?? FormatVersion(builtAt, options.GitShortSha);

        var allClientFiles = Directory.EnumerateFiles(clientDir, "*", SearchOption.AllDirectories)
            .Select(full => (FullPath: full, RelPath: PatchManifest.NormalizePath(Path.GetRelativePath(clientDir, full))))
            .OrderBy(x => x.RelPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RelPath, StringComparer.Ordinal)
            .ToList();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism };

        // Step 1: hash all files in parallel
        var hashed = new HashedFile[allClientFiles.Count];
        Parallel.For(0, allClientFiles.Count, parallelOptions, i =>
        {
            var (fullPath, relPath) = allClientFiles[i];
            var (size, sha256) = HashFile(fullPath);
            hashed[i] = new HashedFile(fullPath, relPath, size, sha256);
        });

        // Step 2: ensure every distinct object exists in objects/<sha256>
        var uniqueByHash = new Dictionary<string, HashedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in hashed)
            uniqueByHash.TryAdd(file.Sha256, file);

        var storedObjects = new ConcurrentDictionary<string, StoredObject>(StringComparer.OrdinalIgnoreCase);
        var objectsWritten = 0;
        var objectsReused = 0;

        Parallel.ForEach(uniqueByHash.Values, parallelOptions, file =>
        {
            var objPath = Path.Combine(objectsDir, file.Sha256);
            if (knownObjects.TryGetValue(file.Sha256, out var known) &&
                File.Exists(objPath) &&
                new FileInfo(objPath).Length == known.CompressedSize)
            {
                storedObjects[file.Sha256] = known;
                Interlocked.Increment(ref objectsReused);
                return;
            }

            var stored = StoreObject(file.FullPath, file.Size, objPath);
            storedObjects[file.Sha256] = stored;
            Interlocked.Increment(ref objectsWritten);
        });

        // Step 3: assemble candidate manifest
        PatchFileEntry? launcherEntry = null;
        var entries = new List<PatchFileEntry>(hashed.Length);
        foreach (var file in hashed)
        {
            var stored = storedObjects[file.Sha256];
            var isLauncher = !string.IsNullOrEmpty(options.LauncherFileName) &&
                             string.Equals(file.RelPath, PatchManifest.NormalizePath(options.LauncherFileName), StringComparison.OrdinalIgnoreCase);
            var entry = new PatchFileEntry
            {
                Path = file.RelPath,
                Size = file.Size,
                Sha256 = file.Sha256,
                CompressedSize = stored.CompressedSize,
                Compression = stored.Compression,
                PlayerOwned = !isLauncher && PlayerOwnedFiles.IsPlayerOwned(file.RelPath),
            };

            if (isLauncher)
                launcherEntry = entry;
            else
                entries.Add(entry);
        }

        var candidate = new PatchManifest
        {
            Version = version,
            BuiltAt = builtAt,
            Start = PatchManifest.NormalizePath(options.StartFileName),
            Launcher = launcherEntry,
            Files = entries,
            Folders = Directory.EnumerateDirectories(clientDir, "*", SearchOption.AllDirectories)
                .Where(d => !Directory.EnumerateFileSystemEntries(d).Any())
                .Select(d => PatchManifest.NormalizePath(Path.GetRelativePath(clientDir, d)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        var diff = PatchManifestDiff.Compute(previousManifest, candidate);

        // Step 4: publish manifest atomically only after all objects exist (and only if content changed)
        PatchManifest activeManifest;
        bool publishedNew;
        if (previousManifest != null && !diff.HasChanges)
        {
            activeManifest = previousManifest;
            publishedNew = false;
        }
        else
        {
            var jsonBytes = PatchManifestJson.SerializeToUtf8Bytes(candidate);
            var versionedManifestPath = Path.Combine(manifestsDir, $"{candidate.Version}.json");
            WriteFileAtomic(versionedManifestPath, jsonBytes);
            WriteFileAtomic(latestManifestPath, jsonBytes);
            activeManifest = candidate;
            publishedNew = true;
        }

        // Step 5: the launcher players download from the site
        string? launcherDownload = null;
        if (activeManifest.Launcher != null)
        {
            launcherDownload = Path.Combine(downloadDir, Path.GetFileName(options.LauncherFileName));
            var source = Path.Combine(clientDir, activeManifest.Launcher.Path);
            var current = new FileInfo(launcherDownload);
            if (!current.Exists || current.Length != activeManifest.Launcher.Size || HashFile(launcherDownload).Sha256 != activeManifest.Launcher.Sha256)
                WriteFileAtomic(launcherDownload, File.ReadAllBytes(source));
        }

        // Step 6: garbage collect unreferenced objects and old manifests
        var gc = GarbageCollect(manifestsDir, objectsDir, activeManifest.Version, options.KeepVersions);

        return new PatchPublishResult
        {
            Manifest = activeManifest,
            Diff = diff,
            PublishedNewManifest = publishedNew,
            ObjectsWritten = objectsWritten,
            ObjectsReused = objectsReused,
            ObjectsGarbageCollected = gc.ObjectsRemoved,
            ManifestsGarbageCollected = gc.ManifestsRemoved,
            LauncherDownloadPath = launcherDownload,
        };
    }

    public static string FormatVersion(DateTimeOffset builtAtUtc, string gitShortSha)
    {
        var sha = string.IsNullOrWhiteSpace(gitShortSha) ? "nogit" : gitShortSha.Trim();
        return $"{builtAtUtc.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{sha}";
    }

    static Dictionary<string, StoredObject> LoadKnownObjects(string manifestsDir, PatchManifest? latest)
    {
        var map = new Dictionary<string, StoredObject>(StringComparer.OrdinalIgnoreCase);

        void Record(PatchManifest m)
        {
            if (m.Launcher != null)
                map.TryAdd(m.Launcher.Sha256, new StoredObject(m.Launcher.Compression, m.Launcher.CompressedSize));
            foreach (var f in m.Files)
                map.TryAdd(f.Sha256, new StoredObject(f.Compression, f.CompressedSize));
        }

        if (latest != null)
            Record(latest);

        if (Directory.Exists(manifestsDir))
        {
            foreach (var file in Directory.EnumerateFiles(manifestsDir, "*.json"))
            {
                try
                {
                    Record(PatchManifestJson.LoadFile(file));
                }
                catch
                {
                    // Ignore unreadable manifests when building reuse cache
                }
            }
        }

        return map;
    }

    static (long Size, string Sha256) HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        var length = stream.Length;
        var hash = SHA256.HashData(stream);
        return (length, Convert.ToHexStringLower(hash));
    }

    static StoredObject StoreObject(string sourcePath, long rawSize, string destPath)
    {
        var dir = Path.GetDirectoryName(destPath)!;
        var gzTemp = Path.Combine(dir, $"{Path.GetFileName(destPath)}.{Guid.NewGuid():N}.gz.tmp");
        try
        {
            long compressedSize;
            using (var input = File.OpenRead(sourcePath))
            using (var output = File.Create(gzTemp))
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                    input.CopyTo(gzip);
                output.Flush();
                compressedSize = output.Length;
            }

            if (compressedSize < rawSize)
            {
                File.Move(gzTemp, destPath, overwrite: true);
                return new StoredObject(PatchCompression.Gzip, compressedSize);
            }

            PatchObjectStore.TryDelete(gzTemp);
            var rawTemp = Path.Combine(dir, $"{Path.GetFileName(destPath)}.{Guid.NewGuid():N}.raw.tmp");
            try
            {
                File.Copy(sourcePath, rawTemp, overwrite: true);
                File.Move(rawTemp, destPath, overwrite: true);
                return new StoredObject(PatchCompression.None, rawSize);
            }
            catch
            {
                PatchObjectStore.TryDelete(rawTemp);
                throw;
            }
        }
        catch
        {
            PatchObjectStore.TryDelete(gzTemp);
            throw;
        }
    }

    static void WriteFileAtomic(string destinationPath, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            PatchObjectStore.TryDelete(tempPath);
            throw;
        }
    }

    static GcStats GarbageCollect(
        string manifestsDir,
        string objectsDir,
        string activeVersion,
        int keepVersions)
    {
        var manifests = new List<(string FilePath, PatchManifest Manifest)>();
        if (Directory.Exists(manifestsDir))
        {
            foreach (var file in Directory.EnumerateFiles(manifestsDir, "*.json"))
            {
                var manifest = PatchManifestJson.LoadFile(file);
                manifests.Add((file, manifest));
            }
        }

        // Keep active version + newest versions up to keepVersions
        var ordered = manifests
            .OrderByDescending(m => string.Equals(m.Manifest.Version, activeVersion, StringComparison.Ordinal))
            .ThenByDescending(m => m.Manifest.BuiltAt)
            .ThenByDescending(m => m.Manifest.Version, StringComparer.Ordinal)
            .ToList();

        var kept = ordered.Take(keepVersions).ToList();
        var toDeleteManifests = ordered.Skip(keepVersions).ToList();

        var manifestsRemoved = 0;
        foreach (var (filePath, _) in toDeleteManifests)
        {
            File.Delete(filePath);
            manifestsRemoved++;
        }

        var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, manifest) in kept)
        {
            foreach (var hash in manifest.AllObjectHashes())
                referencedHashes.Add(hash);
        }

        var objectsRemoved = 0;
        if (Directory.Exists(objectsDir))
        {
            foreach (var file in Directory.EnumerateFiles(objectsDir))
            {
                var name = Path.GetFileName(file);
                if (!IsSha256Hex(name))
                    continue;
                if (!referencedHashes.Contains(name))
                {
                    File.Delete(file);
                    objectsRemoved++;
                }
            }
        }

        return new GcStats(objectsRemoved, manifestsRemoved);
    }

    static bool IsSha256Hex(string s)
    {
        if (s.Length != 64) return false;
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    sealed record HashedFile(string FullPath, string RelPath, long Size, string Sha256);
    sealed record StoredObject(string Compression, long CompressedSize);
    sealed record GcStats(int ObjectsRemoved, int ManifestsRemoved);
}

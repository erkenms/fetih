using System.Security.Cryptography;

namespace Fetih.Patch;

// What a start has to do to bring the client folder in line with a manifest
public sealed record SyncPlan(
    IReadOnlyList<PatchFileEntry> Download,
    IReadOnlyList<string> Delete,
    IReadOnlyList<LocalFile> Current,
    int Hashed)
{
    public bool HasWork => this.Download.Count > 0 || this.Delete.Count > 0;

    public long DownloadBytes => this.Download.DistinctBy(e => e.Sha256, StringComparer.OrdinalIgnoreCase).Sum(e => e.CompressedSize);

    // Compares against the state's sizes and times and hashes only files that don't match it
    // (all of them when repairing). Player-owned files are fetched only when missing. Deletes
    // only what the state's manifest had and this one doesn't.
    public static SyncPlan Create(string root, PatchManifest manifest, LocalState state, bool repair = false)
    {
        var known = new Dictionary<string, LocalFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in state.Files)
            known[PatchManifest.NormalizePath(file.Path)] = file;

        var download = new List<PatchFileEntry>();
        var current = new List<LocalFile>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashed = 0;

        foreach (var entry in manifest.Files)
        {
            var path = PatchManifest.NormalizePath(entry.Path);
            listed.Add(path);
            var info = new FileInfo(FullPath(root, path));

            if (entry.PlayerOwned)
            {
                if (!info.Exists)
                    download.Add(entry);
                continue;
            }

            if (!info.Exists || info.Length != entry.Size)
            {
                download.Add(entry);
                continue;
            }

            if (!repair && known.TryGetValue(path, out var local) && local.Matches(info, entry))
            {
                current.Add(local);
                continue;
            }

            hashed++;
            if (string.Equals(Sha256Of(info.FullName), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                current.Add(LocalFile.Of(path, info, entry.Sha256));
            else
                download.Add(entry);
        }

        var delete = known.Keys
            .Where(path => !listed.Contains(path) && File.Exists(FullPath(root, path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SyncPlan(download, delete, current, hashed);
    }

    // Whether the exe at exePath is the manifest's launcher, by the state's size and time or else by hash
    public static bool LauncherIsCurrent(string exePath, PatchManifest manifest, LocalState state, out LocalFile? local)
    {
        local = null;
        if (manifest.Launcher is not { } entry)
            return true;
        var info = new FileInfo(exePath);
        if (state.Launcher is { } known && known.Matches(info, entry))
        {
            local = known;
            return true;
        }
        if (!info.Exists || info.Length != entry.Size ||
            !string.Equals(Sha256Of(exePath), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            return false;
        local = LocalFile.Of(entry.Path, info, entry.Sha256);
        return true;
    }

    // A manifest path inside root, refusing anything that would climb out of it
    public static string FullPath(string root, string relativePath)
    {
        var path = PatchManifest.NormalizePath(relativePath);
        if (path.Length == 0 || Path.IsPathRooted(path) || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Bad path in manifest: {relativePath}");
        return Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

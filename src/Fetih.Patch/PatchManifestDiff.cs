namespace Fetih.Patch;

public sealed record PatchManifestDiff(
    IReadOnlyList<PatchFileEntry> Added,
    IReadOnlyList<PatchFileEntry> Changed,
    IReadOnlyList<PatchFileEntry> Removed,
    int UnchangedCount,
    bool LauncherChanged,
    bool StartOrFoldersChanged)
{
    public bool HasChanges =>
        Added.Count > 0 ||
        Changed.Count > 0 ||
        Removed.Count > 0 ||
        LauncherChanged ||
        StartOrFoldersChanged;

    public static PatchManifestDiff Compute(PatchManifest? previous, PatchManifest next)
    {
        if (previous == null)
        {
            return new PatchManifestDiff(
                Added: next.Files.ToList(),
                Changed: [],
                Removed: [],
                UnchangedCount: 0,
                LauncherChanged: next.Launcher != null,
                StartOrFoldersChanged: false);
        }

        var prevByPath = new Dictionary<string, PatchFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in previous.Files)
            prevByPath[PatchManifest.NormalizePath(entry.Path)] = entry;

        var nextByPath = new Dictionary<string, PatchFileEntry>(StringComparer.OrdinalIgnoreCase);
        var added = new List<PatchFileEntry>();
        var changed = new List<PatchFileEntry>();
        var unchanged = 0;

        foreach (var entry in next.Files)
        {
            var key = PatchManifest.NormalizePath(entry.Path);
            nextByPath[key] = entry;
            if (!prevByPath.TryGetValue(key, out var oldEntry))
            {
                added.Add(entry);
            }
            else if (!string.Equals(oldEntry.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase) ||
                     oldEntry.Size != entry.Size ||
                     oldEntry.PlayerOwned != entry.PlayerOwned)
            {
                changed.Add(entry);
            }
            else
            {
                unchanged++;
            }
        }

        var removed = new List<PatchFileEntry>();
        foreach (var entry in previous.Files)
        {
            var key = PatchManifest.NormalizePath(entry.Path);
            if (!nextByPath.ContainsKey(key))
                removed.Add(entry);
        }

        var launcherChanged = !EntriesMatch(previous.Launcher, next.Launcher);
        return new PatchManifestDiff(
            Added: added,
            Changed: changed,
            Removed: removed,
            UnchangedCount: unchanged,
            LauncherChanged: launcherChanged,
            StartOrFoldersChanged: !string.Equals(previous.Start, next.Start, StringComparison.OrdinalIgnoreCase) ||
                                   !previous.Folders.SequenceEqual(next.Folders, StringComparer.OrdinalIgnoreCase));
    }

    static bool EntriesMatch(PatchFileEntry? a, PatchFileEntry? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase)
            && a.Size == b.Size
            && string.Equals(PatchManifest.NormalizePath(a.Path), PatchManifest.NormalizePath(b.Path), StringComparison.OrdinalIgnoreCase);
    }
}

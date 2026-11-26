namespace Fetih.Patch;

// Files that Conquer.exe or the player writes at runtime. They ship in the first-download
// zip so defaults and folders exist, but are flagged playerOwned in the manifest so the
// launcher never overwrites a player's copy once it exists.
public static class PlayerOwnedFiles
{
    // Exact relative paths (case-insensitive, '/' separator)
    static readonly HashSet<string> ExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "ini/GameSetUp.ini",      // Resolution, fullscreen/windowed, last group/server, interface
        "ini/c3.wdb",             // GraphicData.dll rebuilds this cache from the ini files at runtime
        "AutoPatch/Update.log",   // AutoPatch history log
        "debug.log",
        "ChatLog.log",
        "leavewordsystem.log",
    };

    // Directory prefixes whose entire subtree is player-owned
    static readonly string[] DirectoryPrefixes =
    [
        "Log/",               // Per-character Setup.ini (volume, hotkeys, PK mode, blacklist) and chat logs
        "debug/",             // Client debug logs (debug/<date>.log) and crash dumps (.dmp)
        "screenshot/",        // Player screenshots (ScreenShot/%u.jpg)
        "AutoPatch/update/",  // AutoPatch staging directory
    ];

    public static bool IsPlayerOwned(string relativePath)
    {
        var normalized = PatchManifest.NormalizePath(relativePath);
        if (ExactPaths.Contains(normalized))
            return true;

        foreach (var prefix in DirectoryPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Root-level *.log files (e.g. p2pTemp1.log, p2plook1..3.log)
        if (!normalized.Contains('/') && normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

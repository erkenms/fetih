using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetih.Patch;

// What the launcher last put on disk, so a normal start compares sizes and times instead of
// hashing. Kept in .launcher/state.json inside the client folder.
public sealed record LocalState
{
    public const string Folder = ".launcher";
    public const string FileName = "state.json";

    // The manifest last applied in full; null after an interrupted sync
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    // Every file of that manifest the launcher keeps in line (not the player-owned ones)
    [JsonPropertyName("files")]
    public IReadOnlyList<LocalFile> Files { get; init; } = [];

    [JsonPropertyName("launcher")]
    public LocalFile? Launcher { get; init; }

    // GameSetUp.ini got its first-start settings
    [JsonPropertyName("gameSetUp")]
    public bool GameSetUp { get; init; }

    public static string PathIn(string root) => Path.Combine(root, Folder, FileName);

    public static LocalState Load(string root)
    {
        try
        {
            var path = PathIn(root);
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllBytes(path), LauncherJsonContext.Default.LocalState) ?? new();
        }
        catch (JsonException)
        {
            // A broken state only costs hashing everything once
        }
        return new();
    }

    public void Save(string root)
    {
        var path = PathIn(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, LauncherJsonContext.Default.LocalState));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed record LocalFile
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }

    // Last write time, UTC ticks
    [JsonPropertyName("mtime")]
    public required long MTime { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    public static LocalFile Of(string path, FileInfo info, string sha256) => new()
    {
        Path = path,
        Size = info.Length,
        MTime = info.LastWriteTimeUtc.Ticks,
        Sha256 = sha256,
    };

    public bool Matches(FileInfo info, PatchFileEntry entry) =>
        info.Exists && info.Length == this.Size && info.LastWriteTimeUtc.Ticks == this.MTime &&
        info.Length == entry.Size && string.Equals(this.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase);
}

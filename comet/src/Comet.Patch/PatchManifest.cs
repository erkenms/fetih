using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comet.Patch;

public static class PatchCompression
{
    public const string None = "none";
    public const string Gzip = "gzip";
    public const string Brotli = "br";
}

public sealed record PatchFileEntry
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("compressedSize")]
    public required long CompressedSize { get; init; }

    [JsonPropertyName("compression")]
    public required string Compression { get; init; }

    [JsonPropertyName("playerOwned")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PlayerOwned { get; init; }
}

public sealed record PatchManifest
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("builtAt")]
    public required DateTimeOffset BuiltAt { get; init; }

    [JsonPropertyName("minLauncherVersion")]
    public required string MinLauncherVersion { get; init; }

    [JsonPropertyName("launcher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PatchFileEntry? Launcher { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<PatchFileEntry> Files { get; init; }

    public IEnumerable<string> AllObjectHashes()
    {
        if (Launcher != null)
            yield return Launcher.Sha256;
        foreach (var file in Files)
            yield return file.Sha256;
    }

    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

[JsonSerializable(typeof(PatchManifest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class PatchJsonContext : JsonSerializerContext;

public static class PatchManifestJson
{
    public static byte[] SerializeToUtf8Bytes(PatchManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, PatchJsonContext.Default.PatchManifest);

    public static string Serialize(PatchManifest manifest) =>
        JsonSerializer.Serialize(manifest, PatchJsonContext.Default.PatchManifest);

    public static PatchManifest Deserialize(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, PatchJsonContext.Default.PatchManifest)
        ?? throw new InvalidDataException("Manifest JSON deserialized to null.");

    public static PatchManifest Deserialize(string json) =>
        JsonSerializer.Deserialize(json, PatchJsonContext.Default.PatchManifest)
        ?? throw new InvalidDataException("Manifest JSON deserialized to null.");

    public static PatchManifest LoadFile(string path) =>
        Deserialize(File.ReadAllBytes(path));
}

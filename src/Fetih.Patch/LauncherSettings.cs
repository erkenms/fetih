using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetih.Patch;

// launcher.json, next to Fetih.exe; the client build writes it and bakes the same into the exe
public sealed record LauncherSettings
{
    public const string FileName = "launcher.json";

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    public Uri PatchBase => new($"{this.Url.TrimEnd('/')}/patch/");

    public Uri News => new($"{this.Url.TrimEnd('/')}/haberler?handler=launcher");

    public static LauncherSettings Parse(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, LauncherJsonContext.Default.LauncherSettings)
        ?? throw new InvalidDataException("launcher.json is empty.");

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, LauncherJsonContext.Default.LauncherSettings);
}

[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(LocalState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public sealed partial class LauncherJsonContext : JsonSerializerContext;

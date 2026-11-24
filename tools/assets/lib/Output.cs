using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

// The output folder. .build.json remembers, per output file, the stamp of the inputs it was
// made from; an image whose stamp is unchanged isn't decoded again. Files a run no longer
// produces are deleted at the end.
sealed class Output
{
    public string Root { get; }
    public int Written => written;
    public int Skipped => skipped;
    public ConcurrentBag<string> Errors { get; } = new();

    private readonly Dictionary<string, string> previous;
    private readonly ConcurrentDictionary<string, string> current = new();
    private readonly string manifestPath;
    private readonly string version;
    private int written, skipped;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new(Json)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public Output(string root, string version)
    {
        Root = root;
        this.version = version;
        manifestPath = Path.Combine(root, ".build.json");
        previous = new Dictionary<string, string>();
        try
        {
            if (File.Exists(manifestPath))
                previous = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath)) ?? previous;
        }
        catch (JsonException)
        {
        }
    }

    // Writes the image at `path` (relative, .png) unless it's current. Returns false when the
    // source can't be decoded; the error is collected.
    public bool Image(string path, string stamp, Func<Rgba> load)
    {
        stamp = $"{version}|{stamp}";
        var full = Full(path);
        if (previous.TryGetValue(path, out var old) && old == stamp && File.Exists(full))
        {
            current[path] = stamp;
            Interlocked.Increment(ref skipped);
            return true;
        }
        try
        {
            var png = Images.Png(load());
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full + ".tmp", png);
            File.Move(full + ".tmp", full, overwrite: true);
            current[path] = stamp;
            Interlocked.Increment(ref written);
            return true;
        }
        catch (Exception e) when (e is not IOException and not UnauthorizedAccessException)
        {
            Errors.Add($"{path}: {e.Message}");
            return false;
        }
    }

    // Writes JSON, touching the file only when its content changed. Null fields are left out,
    // and with omitZero also zeros and false.
    public void Write<T>(string path, T value, bool omitZero = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, omitZero ? Compact : Json);
        var full = Full(path);
        current[path] = "json";
        if (File.Exists(full) && File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
        {
            Interlocked.Increment(ref skipped);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full + ".tmp", bytes);
        File.Move(full + ".tmp", full, overwrite: true);
        Interlocked.Increment(ref written);
    }

    // Deletes what earlier runs wrote and this one didn't, then saves the manifest
    public int Finish()
    {
        var removed = 0;
        foreach (var path in previous.Keys.Where(p => !current.ContainsKey(p)))
        {
            var full = Full(path);
            if (File.Exists(full))
            {
                File.Delete(full);
                removed++;
            }
        }
        var sorted = new SortedDictionary<string, string>(current, StringComparer.Ordinal);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }));
        return removed;
    }

    private string Full(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
}

using System.Text;

// A file of the client by its path ("data/ItemMinIcon/111300.dds"), found loose in the client
// folder or in one of its .wdf packages. Paths are compared without case, as Windows does.
sealed class ClientFiles : IDisposable
{
    public string Root { get; }
    private readonly Dictionary<string, string> loose = new(StringComparer.Ordinal);
    private readonly List<(WdfPackage Package, string Stamp)> packages = new();

    public static readonly Encoding Gbk = CreateGbk();

    public ClientFiles(string root)
    {
        Root = root;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            loose.TryAdd(WdfPackage.Normalize(System.IO.Path.GetRelativePath(root, path)), path);
        foreach (var name in new[] { "data.wdf", "c3.wdf" })
            if (loose.TryGetValue(name, out var path))
            {
                var info = new FileInfo(path);
                packages.Add((new WdfPackage(path), $"{name}:{info.Length}:{info.LastWriteTimeUtc.Ticks}"));
            }
    }

    // What the file's content depends on, to tell whether an output made from it is stale
    public string? Stamp(string path)
    {
        var key = WdfPackage.Normalize(path);
        if (loose.TryGetValue(key, out var full))
        {
            var info = new FileInfo(full);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        foreach (var (package, stamp) in packages)
            if (package.TryGet(key, out var entry))
                return $"{stamp}:{entry.Offset}:{entry.Size}";
        return null;
    }

    public bool Exists(string path) => Stamp(path) != null;

    public byte[]? Read(string path)
    {
        var key = WdfPackage.Normalize(path);
        if (loose.TryGetValue(key, out var full))
            return File.ReadAllBytes(full);
        foreach (var (package, _) in packages)
            if (package.TryGet(key, out var entry))
                return package.Read(entry);
        return null;
    }

    public void Dispose()
    {
        foreach (var (package, _) in packages)
            package.Dispose();
    }

    private static Encoding CreateGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }
}

// An .ani file from the client's ani/ folder: [Name] sections, each with FrameAmount and
// Frame0..FrameN paths. Names compare without case.
static class Ani
{
    public static Dictionary<string, List<string>> Parse(byte[] data)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? frames = null;
        foreach (var raw in ClientFiles.Gbk.GetString(data).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                frames = end > 1 ? new List<string>() : null;
                if (frames != null)
                    sections.TryAdd(line[1..end].Trim(), frames);
            }
            else if (frames != null && line.StartsWith("Frame", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("FrameAmount", StringComparison.OrdinalIgnoreCase))
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    frames.Add(line[(eq + 1)..].Trim());
            }
        }
        return sections;
    }
}

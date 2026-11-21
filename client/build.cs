// Builds the Fetih client from the original 5619 client:
//
//   dotnet run client/build.cs [source] [output]
//
// Defaults: _CLIENT/5619 into _CLIENT/fetih, both next to this folder's parent. The
// output mirrors the source, minus the paths in delete.txt, plus everything under files/
// (added or replacing), with the byte patches in patches.json applied last. Patches name
// the bytes they expect, so a patch that no longer fits its file fails the build instead
// of corrupting it. Unchanged files aren't copied again, and files the build no longer
// produces are removed from the output.
#:property PublishAot=false
using System.Runtime.CompilerServices;
using System.Text.Json;

var here = Path.GetDirectoryName(ScriptPath())!;
var root = Path.GetDirectoryName(here)!;
var source = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root, "_CLIENT", "5619"));
var output = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(root, "_CLIENT", "fetih"));
var overlay = Path.Combine(here, "files");
if (!Directory.Exists(source))
    return Fail($"No client at {source}");
// Through links too: a link from the output to the source would have the build edit it
var realSource = RealPath(source);
var realOutput = RealPath(output);
if (realOutput == realSource || realOutput.StartsWith(realSource + Path.DirectorySeparatorChar))
    return Fail(output == realOutput
        ? "The output can't be inside the source client"
        : $"{output} is a link to the source client; remove the link to build into it");

// Client paths are compared without case, as Windows does
var comparer = StringComparer.OrdinalIgnoreCase;
var files = new Dictionary<string, string>(comparer);
foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    files[Relative(source, path)] = path;
// Folders too, since the client keeps some empty ones (screenshot, AutoPatch/update)
var folders = new HashSet<string>(
    Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Select(d => Relative(source, d)), comparer);

var deleted = 0;
foreach (var line in File.ReadAllLines(Path.Combine(here, "delete.txt")))
{
    var entry = line.Split('#')[0].Trim().Replace('\\', '/');
    if (entry.Length == 0)
        continue;
    var prefix = entry.TrimEnd('/');
    bool Under(string k) => comparer.Equals(k, prefix) || k.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    var removed = files.Keys.Where(Under).ToList();
    if (removed.Count == 0 && folders.RemoveWhere(Under) == 0)
        return Fail($"delete.txt: {entry} isn't in the source client");
    removed.ForEach(k => files.Remove(k));
    folders.RemoveWhere(Under);
    deleted += removed.Count;
}

var replaced = 0;
var added = 0;
if (Directory.Exists(overlay))
    foreach (var path in Directory.EnumerateFiles(overlay, "*", SearchOption.AllDirectories))
    {
        if (Path.GetFileName(path) is ".DS_Store" or ".gitkeep")
            continue;
        var key = Relative(overlay, path);
        if (files.ContainsKey(key)) replaced++; else added++;
        files.Remove(key);
        files[key] = path;
    }
foreach (var key in files.Keys.ToList())
    for (var dir = Path.GetDirectoryName(key)?.Replace('\\', '/'); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir)?.Replace('\\', '/'))
        folders.Add(dir);
foreach (var key in folders)
    Directory.CreateDirectory(Path.Combine(output, key));

var patches = JsonSerializer.Deserialize<List<Patch>>(
    File.ReadAllText(Path.Combine(here, "patches.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })!;
var patchesByFile = patches.GroupBy(p => p.File.Replace('\\', '/'), comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);
foreach (var file in patchesByFile.Keys)
    if (!files.ContainsKey(file))
        return Fail($"patches.json: {file} isn't in the client");

var written = 0;
foreach (var (key, from) in files)
{
    var to = Path.Combine(output, key);
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    if (patchesByFile.TryGetValue(key, out var filePatches))
    {
        var bytes = File.ReadAllBytes(from);
        foreach (var patch in filePatches)
        {
            var error = patch.Apply(bytes);
            if (error != null)
                return Fail($"patches.json: {key} at {patch.Offset} ({patch.Reason}): {error}");
        }
        if (!File.Exists(to) || !File.ReadAllBytes(to).AsSpan().SequenceEqual(bytes))
        {
            File.WriteAllBytes(to, bytes);
            written++;
        }
        continue;
    }

    var a = new FileInfo(from);
    var b = new FileInfo(to);
    if (b.Exists && a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc)
        continue;
    File.Copy(from, to, overwrite: true);
    File.SetLastWriteTimeUtc(to, a.LastWriteTimeUtc);
    written++;
}

var stale = 0;
if (Directory.Exists(output))
{
    foreach (var path in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).ToList())
        if (!files.ContainsKey(Relative(output, path)))
        {
            File.Delete(path);
            stale++;
        }
    foreach (var dir in Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories)
                 .OrderByDescending(d => d.Length))
        if (!folders.Contains(Relative(output, dir)) && !Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
}

Console.WriteLine($"{output}: {files.Count} files ({added} added, {replaced} replaced, {deleted} deleted, " +
    $"{patches.Count} byte patches); {written} written, {stale} stale removed");
return 0;

static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string RealPath(string path) =>
    Directory.Exists(path) ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;

static string ScriptPath([CallerFilePath] string path = "") => path;

// Bytes as hex, spaces allowed: "ff 75 10"
sealed record Patch(string File, string Offset, string Original, string Patched, string Reason)
{
    public string? Apply(byte[] bytes)
    {
        var offset = Convert.ToInt64(this.Offset, 16);
        var original = Convert.FromHexString(this.Original.Replace(" ", ""));
        var patched = Convert.FromHexString(this.Patched.Replace(" ", ""));
        if (original.Length != patched.Length)
            return "original and patched bytes differ in length";
        if (offset < 0 || offset + original.Length > bytes.Length)
            return "past the end of the file";
        var at = bytes.AsSpan((int)offset, original.Length);
        if (!at.SequenceEqual(original))
            return $"expected {Convert.ToHexString(original)}, found {Convert.ToHexString(at)}";
        patched.CopyTo(at);
        return null;
    }
}

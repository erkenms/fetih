// Builds the Fetih client from the original 5619 client:
//
//   dotnet run client/build.cs [source] [output]
//
// Defaults: _CLIENT/5619 into _CLIENT/fetih, both in this folder's parent. The
// output mirrors the source, minus the paths in delete.txt, plus everything under files/
// (added or replacing), with the byte patches in patches.json applied last. Patches name
// the bytes they expect, so a patch that no longer fits its file fails the build instead
// of corrupting it. Unchanged files aren't copied again, and files the build no longer
// produces are removed from the output.
//
// Server.dat and a few ini/*.dat files are RSA-signed with TQ's private key, and the
// client reads them with the public modulus in Conquer.exe. The build swaps that modulus
// for one of its own key (client/rsa.pem, made on first build and kept out of git) and
// signs those files again with it. A file under files/ that stands in for one of them is
// plaintext; get one to edit with:
//
//   dotnet run client/build.cs decrypt <file> > client/files/<file>
#:property PublishAot=false
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

var here = Path.GetDirectoryName(ScriptPath())!;
var root = Path.GetDirectoryName(here)!;
var defaultSource = Path.Combine(root, "_CLIENT", "5619");
var keyPath = Path.Combine(here, "rsa.pem");
if (args is ["decrypt", var encryptedPath])
{
    var exe = File.ReadAllBytes(Path.Combine(defaultSource, "Conquer.exe"));
    var moduli = new List<BigInteger> { Rsa.ReadModulus(exe) ?? throw new Exception("No RSA modulus in Conquer.exe") };
    if (File.Exists(keyPath))
        moduli.Add(Rsa.LoadKey(keyPath).N);
    var data = File.ReadAllBytes(encryptedPath);
    var plain = moduli.Select(n => Rsa.Decrypt(data, n)).FirstOrDefault(p => p != null);
    if (plain == null)
        return Fail($"{encryptedPath} isn't signed with TQ's key or {keyPath}");
    using var stdout = Console.OpenStandardOutput();
    stdout.Write(plain);
    return 0;
}
var source = Path.GetFullPath(args.Length > 0 ? args[0] : defaultSource);
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

const string exeName = "Conquer.exe";
if (!files.ContainsKey(exeName))
    return Fail($"No {exeName} in the client");
var tqModulus = Rsa.ReadModulus(File.ReadAllBytes(Path.Combine(source, exeName)));
if (tqModulus == null)
    return Fail($"No RSA modulus in {exeName}");
if (!File.Exists(keyPath))
{
    using var rsa = RSA.Create(2048);
    File.WriteAllText(keyPath, rsa.ExportRSAPrivateKeyPem());
    Console.WriteLine($"Made a new RSA key at {keyPath}");
}
var ourKey = Rsa.LoadKey(keyPath);
// The signed files, as the source client has them, decrypted
var signed = new Dictionary<string, byte[]>(comparer);
foreach (var path in Directory.EnumerateFiles(source, "*.dat", SearchOption.AllDirectories))
    if (Rsa.Decrypt(File.ReadAllBytes(path), tqModulus.Value) is { } plain)
        signed[Relative(source, path)] = plain;

var written = 0;
foreach (var (key, from) in files)
{
    var to = Path.Combine(output, key);
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    byte[]? bytes = null;
    if (comparer.Equals(key, exeName))
    {
        bytes = File.ReadAllBytes(from);
        Rsa.WriteModulus(bytes, ourKey.N);
    }
    if (signed.TryGetValue(key, out var plaintext))
        bytes = Rsa.Encrypt(from.StartsWith(overlay + Path.DirectorySeparatorChar) ? File.ReadAllBytes(from) : plaintext, ourKey);
    if (patchesByFile.TryGetValue(key, out var filePatches))
    {
        bytes ??= File.ReadAllBytes(from);
        foreach (var patch in filePatches)
        {
            var error = patch.Apply(bytes);
            if (error != null)
                return Fail($"patches.json: {key} at {patch.Offset} ({patch.Reason}): {error}");
        }
    }
    if (bytes != null)
    {
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
    $"{patches.Count} byte patches, {signed.Count} signed again); {written} written, {stale} stale removed");
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

static class Rsa
{
    // The client builds its 2048-bit modulus at run time from 64 MOV [ebp+disp], imm32
    // instructions (C7 85 disp32 imm32, then C7 45 disp8 imm32). Of the stored words s,
    // s[i] ^ s[i + 1] is modulus word (17 + i) % 64 and s[63] is word 16, words counted
    // from the most significant.
    const uint FirstStored = 0xA0AF3D5C, LastStored = 0x2220F2A7;
    const int Rotation = 17;
    const int Block = 256;
    // A block holds 00 01 FF.. 00 and then the data, with at least 8 bytes of FF
    const int BlockData = Block - 11;
    static readonly BigInteger Exponent = 65537;

    public static BigInteger? ReadModulus(byte[] exe)
    {
        if (FindModulus(exe) is not { } at)
            return null;
        var stored = at.Select(o => BitConverter.ToUInt32(exe, o)).ToArray();
        var words = new uint[64];
        for (var i = 0; i < 63; i++)
            words[(Rotation + i) % 64] = stored[i] ^ stored[i + 1];
        words[Rotation - 1] = stored[63];
        var bytes = words.SelectMany(w => new[] { (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w }).ToArray();
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    public static void WriteModulus(byte[] exe, BigInteger n)
    {
        var at = FindModulus(exe) ?? throw new InvalidOperationException("No RSA modulus to replace");
        var bytes = ToBlock(n);
        var words = Enumerable.Range(0, 64).Select(i => (uint)(bytes[4 * i] << 24 | bytes[4 * i + 1] << 16 | bytes[4 * i + 2] << 8 | bytes[4 * i + 3])).ToArray();
        var stored = new uint[64];
        stored[63] = words[Rotation - 1];
        for (var i = 62; i >= 0; i--)
            stored[i] = words[(Rotation + i) % 64] ^ stored[i + 1];
        for (var i = 0; i < 64; i++)
            BitConverter.TryWriteBytes(exe.AsSpan(at[i]), stored[i]);
    }

    static int[]? FindModulus(byte[] exe)
    {
        var first = BitConverter.GetBytes(FirstStored);
        for (var start = exe.AsSpan().IndexOf(first); start >= 0;)
        {
            var at = new int[64];
            at[0] = start;
            var count = 1;
            for (; count < 64; count++)
            {
                var next = at[count - 1] + 4;
                if (next + 2 > exe.Length || exe[next] != 0xC7 || exe[next + 1] is not (0x85 or 0x45))
                    break;
                at[count] = next + (exe[next + 1] == 0x85 ? 6 : 3);
            }
            if (count == 64 && at[63] + 4 <= exe.Length && BitConverter.ToUInt32(exe, at[63]) == LastStored)
                return at;
            var rest = exe.AsSpan(start + 1).IndexOf(first);
            start = rest < 0 ? -1 : start + 1 + rest;
        }
        return null;
    }

    // Gzip, cut into blocks with type 1 padding, each raised to the private exponent
    public static byte[]? Decrypt(byte[] data, BigInteger n)
    {
        if (data.Length == 0 || data.Length % Block != 0)
            return null;
        var payload = new MemoryStream();
        for (var i = 0; i < data.Length; i += Block)
        {
            var block = ToBlock(BigInteger.ModPow(new BigInteger(data.AsSpan(i, Block), isUnsigned: true, isBigEndian: true), Exponent, n));
            var end = block.AsSpan(2).IndexOf((byte)0);
            if (block[0] != 0 || block[1] != 1 || end < 8)
                return null;
            payload.Write(block, 2 + end + 1, Block - 3 - end);
        }
        payload.Position = 0;
        var plain = new MemoryStream();
        try
        {
            using var gzip = new GZipStream(payload, CompressionMode.Decompress);
            gzip.CopyTo(plain);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return plain.ToArray();
    }

    public static byte[] Encrypt(byte[] plain, Key key)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize))
            gzip.Write(plain);
        var data = compressed.ToArray();
        var output = new MemoryStream();
        for (var i = 0; i < data.Length; i += BlockData)
        {
            var chunk = data.AsSpan(i, Math.Min(BlockData, data.Length - i));
            var block = new byte[Block];
            block[1] = 1;
            block.AsSpan(2, Block - 3 - chunk.Length).Fill(0xFF);
            chunk.CopyTo(block.AsSpan(Block - chunk.Length));
            output.Write(ToBlock(BigInteger.ModPow(new BigInteger(block, isUnsigned: true, isBigEndian: true), key.D, key.N)));
        }
        return output.ToArray();
    }

    public static Key LoadKey(string path)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        var p = rsa.ExportParameters(includePrivateParameters: true);
        if (p.Modulus!.Length != Block || new BigInteger(p.Exponent, isUnsigned: true, isBigEndian: true) != Exponent)
            throw new InvalidOperationException($"{path} must be a 2048-bit RSA key with exponent 65537");
        return new Key(new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true), new BigInteger(p.D, isUnsigned: true, isBigEndian: true));
    }

    static byte[] ToBlock(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var block = new byte[Block];
        bytes.CopyTo(block, Block - bytes.Length);
        return block;
    }

    public sealed record Key(BigInteger N, BigInteger D);
}

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

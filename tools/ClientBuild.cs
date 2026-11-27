using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class ClientBuild
{
    static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static int Decrypt(string clientInputsDir, string baseClientDir, string encryptedPath)
    {
        var keyPath = Path.Combine(clientInputsDir, "rsa.pem");
        var moduli = new List<BigInteger>();
        var exePath = Path.Combine(baseClientDir, "Conquer.exe");
        if (File.Exists(exePath))
        {
            var exe = File.ReadAllBytes(exePath);
            if (Rsa.ReadModulus(exe) is { } tqModulus)
                moduli.Add(tqModulus);
        }
        if (File.Exists(keyPath))
            moduli.Add(Rsa.LoadKey(keyPath).N);
        if (moduli.Count == 0)
            return Fail($"Neither {exePath} nor {keyPath} exists to provide an RSA modulus");

        var data = File.ReadAllBytes(encryptedPath);
        var plain = moduli.Select(n => Rsa.Decrypt(data, n)).FirstOrDefault(p => p != null);
        if (plain == null)
            return Fail($"{encryptedPath} isn't signed with TQ's key or {keyPath}");
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(plain);
        return 0;
    }

    public static int Run(string clientInputsDir, string source, string output, string? accountIp, IReadOnlyDictionary<string, byte[]> generated)
    {
        if (string.IsNullOrWhiteSpace(accountIp))
            return Fail("Account server IP is required: pass --ip <ipv4> or set FETIH_ACCOUNT_IP");
        if (!IPAddress.TryParse(accountIp, out var parsedIp) || parsedIp.AddressFamily != AddressFamily.InterNetwork || accountIp.Length > 15)
            return Fail($"ServerIP must be an IPv4 address (max 15 characters), got '{accountIp}'");

        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        var overlay = Path.Combine(clientInputsDir, "files");
        var keyPath = Path.Combine(clientInputsDir, "rsa.pem");
        if (!Directory.Exists(source))
            return Fail($"No client at {source}");
        if (!File.Exists(keyPath))
            return Fail($"Missing RSA key at {keyPath}");

        // Through links too: a link from the output to the source would have the build edit it
        var realSource = RealPath(source);
        var realOutput = RealPath(output);
        if (realOutput == realSource || realOutput.StartsWith(realSource + Path.DirectorySeparatorChar))
            return Fail(output == realOutput
                ? "The output can't be inside the source client"
                : $"{output} is a link to the source client; remove the link to build into it");

        var files = new Dictionary<string, string>(Comparer);
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            files[Relative(source, path)] = path;
        // Folders too, since the client keeps some empty ones (screenshot, AutoPatch/update)
        var folders = new HashSet<string>(
            Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Select(d => Relative(source, d)), Comparer);

        var deleted = 0;
        foreach (var line in File.ReadAllLines(Path.Combine(clientInputsDir, "delete.txt")))
        {
            var entry = line.Split('#')[0].Trim().Replace('\\', '/');
            if (entry.Length == 0)
                continue;
            var prefix = entry.TrimEnd('/');
            bool Under(string k) => Comparer.Equals(k, prefix) || k.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
            var removed = files.Keys.Where(Under).ToList();
            if (removed.Count == 0 && folders.RemoveWhere(Under) == 0)
                return Fail($"delete.txt: {entry} isn't in the source client");
            removed.ForEach(k => files.Remove(k));
            folders.RemoveWhere(Under);
            deleted += removed.Count;
        }

        const string serverDatName = "Server.dat";
        var replaced = 0;
        var added = 0;
        if (Directory.Exists(overlay))
            foreach (var path in Directory.EnumerateFiles(overlay, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) is ".DS_Store" or ".gitkeep")
                    continue;
                var key = Relative(overlay, path);
                if (Comparer.Equals(key, serverDatName))
                    return Fail($"Remove {path}: Server.dat is generated from client/servers.json");
                if (files.ContainsKey(key)) replaced++; else added++;
                files.Remove(key);
                files[key] = path;
            }
        foreach (var key in generated.Keys)
        {
            if (files.ContainsKey(key))
                return Fail($"{key} is built (the launcher); remove it from the client");
            files[key] = key;
        }
        foreach (var key in files.Keys.ToList())
            for (var dir = Path.GetDirectoryName(key)?.Replace('\\', '/'); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir)?.Replace('\\', '/'))
                folders.Add(dir);
        foreach (var key in folders)
            Directory.CreateDirectory(Path.Combine(output, key));

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var serverListPath = Path.Combine(clientInputsDir, "servers.json");
        var servers = JsonSerializer.Deserialize<ServerList>(File.ReadAllText(serverListPath), jsonOptions)
            ?? throw new InvalidOperationException($"Invalid {serverListPath}");
        if (servers.Realms.Count == 0)
            return Fail($"{serverListPath}: at least one realm is required");
        if (!files.ContainsKey(serverDatName))
            return Fail($"No {serverDatName} in the client");

        var patches = JsonSerializer.Deserialize<List<Patch>>(File.ReadAllText(Path.Combine(clientInputsDir, "patches.json")), jsonOptions)!;
        var patchesByFile = patches.GroupBy(p => p.File.Replace('\\', '/'), Comparer).ToDictionary(g => g.Key, g => g.ToList(), Comparer);
        foreach (var file in patchesByFile.Keys)
            if (!files.ContainsKey(file))
                return Fail($"patches.json: {file} isn't in the client");

        var flashBitmaps = JsonSerializer.Deserialize<List<FlashBitmap>>(File.ReadAllText(Path.Combine(clientInputsDir, "flash.json")), jsonOptions)!;
        var flashByFile = flashBitmaps.GroupBy(b => b.File.Replace('\\', '/'), Comparer).ToDictionary(g => g.Key, g => g.ToList(), Comparer);
        foreach (var file in flashByFile.Keys)
            if (!files.ContainsKey(file))
                return Fail($"flash.json: {file} isn't in the client");

        const string itemTypeName = "ini/itemtype.dat";
        var itemTypeEdits = JsonSerializer.Deserialize<List<ItemTypeEdit>>(File.ReadAllText(Path.Combine(clientInputsDir, "itemtype.json")), jsonOptions)!;
        if (itemTypeEdits.Count > 0 && !files.ContainsKey(itemTypeName))
            return Fail($"itemtype.json: {itemTypeName} isn't in the client");

        const string exeName = "Conquer.exe";
        if (!files.ContainsKey(exeName))
            return Fail($"No {exeName} in the client");
        var tqModulus = Rsa.ReadModulus(File.ReadAllBytes(Path.Combine(source, exeName)));
        if (tqModulus == null)
            return Fail($"No RSA modulus in {exeName}");
        var ourKey = Rsa.LoadKey(keyPath);

        // The signed files, as the source client has them, decrypted
        var signed = new Dictionary<string, byte[]>(Comparer);
        foreach (var path in Directory.EnumerateFiles(source, "*.dat", SearchOption.AllDirectories))
            if (Rsa.Decrypt(File.ReadAllBytes(path), tqModulus.Value) is { } plain)
                signed[Relative(source, path)] = plain;

        var serverDatPlain = Encoding.ASCII.GetBytes(servers.Render(accountIp));

        var written = 0;
        foreach (var (key, from) in files)
        {
            var to = Path.Combine(output, key);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            byte[]? bytes = generated.GetValueOrDefault(key);
            if (Comparer.Equals(key, exeName))
            {
                bytes = File.ReadAllBytes(from);
                Rsa.WriteModulus(bytes, ourKey.N);
            }
            if (Comparer.Equals(key, serverDatName))
                bytes = Rsa.Encrypt(serverDatPlain, ourKey);
            else if (signed.TryGetValue(key, out var plaintext))
                bytes = Rsa.Encrypt(from.StartsWith(overlay + Path.DirectorySeparatorChar) ? File.ReadAllBytes(from) : plaintext, ourKey);
            if (Comparer.Equals(key, itemTypeName) && itemTypeEdits.Count > 0)
            {
                bytes ??= File.ReadAllBytes(from);
                var (edited, error) = ItemTypeEdit.ApplyAll(bytes, itemTypeEdits);
                if (error != null)
                    return Fail($"itemtype.json: {error}");
                bytes = edited;
            }
            if (flashByFile.TryGetValue(key, out var fileBitmaps))
            {
                var (swapped, error) = FlashBitmap.ApplyAll(bytes ?? File.ReadAllBytes(from), fileBitmaps, clientInputsDir);
                if (error != null)
                    return Fail($"flash.json: {key}: {error}");
                bytes = swapped;
            }
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
            $"{generated.Count} built, {patches.Count} byte patches, {itemTypeEdits.Count} itemtype edits, {flashBitmaps.Count} flash bitmaps, {signed.Count} signed again); {written} written, {stale} stale removed");
        return 0;
    }

    static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    static string RealPath(string path) =>
        Directory.Exists(path) ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;

    sealed record ServerList(ServerGroup Group, List<ServerRealm> Realms)
    {
        public string Render(string ip)
        {
            var sb = new StringBuilder();
            void Line(string s) => sb.Append(s).Append("\r\n");

            Line("<?xml version=\"1.0\"?>");
            Line("<mysqldump xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            Line("<database name=\"cq2\">");
            Line("\t<table_structure name=\"outenserver\">");
            Line("\t\t<field Field=\"id\" Type=\"smallint(1) unsigned zerofill\" Null=\"NO\" Key=\"PRI\" Default=\"0\" Extra=\"\" />");
            Line("\t\t<field Field=\"ServerName\" Type=\"varchar(32)\" Null=\"YES\" Key=\"\" Extra=\"\" />");
            Line("\t\t<field Field=\"ServerIP\" Type=\"varchar(15)\" Null=\"YES\" Key=\"\" Extra=\"\" />");
            Line("\t\t<field Field=\"ServerPort\" Type=\"int(1) unsigned\" Null=\"YES\" Key=\"\" Extra=\"\" />");
            Line("\t\t<field Field=\"FlashName\" Type=\"varchar(32)\" Null=\"YES\" Key=\"\" Extra=\"\" />");
            Line("\t\t<field Field=\"FlashIcon\" Type=\"varchar(32)\" Null=\"YES\" Key=\"\" Extra=\"\" />");
            Line("\t\t<field Field=\"FlashHint\" Type=\"varchar(128)\" Null=\"YES\" Key=\"\" Extra=\"\" />");
            Line("\t\t<field Field=\"Child\" Type=\"tinyint(1) unsigned zerofill\" Null=\"NO\" Key=\"\" Default=\"0\" Extra=\"\" />");
            Line("\t\t<key Table=\"outenserver\" Non_unique=\"0\" Key_name=\"id\" Seq_in_index=\"1\" Column_name=\"id\" Collation=\"A\" Cardinality=\"112\" Null=\"\" Index_type=\"BTREE\" Comment=\"\" />");
            Line("\t\t<options Name=\"outenserver\" Engine=\"MyISAM\" Version=\"10\" Row_format=\"Dynamic\" Rows=\"112\" Avg_row_length=\"57\" Data_length=\"6476\" Max_data_length=\"281474976710655\" Index_length=\"2048\" Data_free=\"0\" Create_time=\"2008-11-26 08:33:09\" Update_time=\"2012-05-25 06:43:56\" Collation=\"latin1_swedish_ci\" Create_options=\"\" Comment=\"\" />");
            Line("\t</table_structure>");
            Line("\t<table_data name=\"outenserver\">");

            void Row(int id, string serverName, string serverIp, int serverPort, string flashName, string flashIcon, string flashHint, int child)
            {
                Line("\t<row>");
                Line($"\t\t<field name=\"id\">{id}</field>");
                Line($"\t\t<field name=\"ServerName\">{Esc(serverName)}</field>");
                Line($"\t\t<field name=\"ServerIP\">{Esc(serverIp)}</field>");
                Line($"\t\t<field name=\"ServerPort\">{serverPort}</field>");
                Line($"\t\t<field name=\"FlashName\">{Esc(flashName)}</field>");
                Line($"\t\t<field name=\"FlashIcon\">{Esc(flashIcon)}</field>");
                Line($"\t\t<field name=\"FlashHint\">{Esc(flashHint)}</field>");
                Line($"\t\t<field name=\"Child\">{child}</field>");
                Line("\t</row>");
            }

            Row(0, "", "", 0, "", "", "", 1);
            Row(1, "", "", 0, Group.FlashName, Group.FlashIcon, Group.FlashHint ?? "", Realms.Count);
            for (var i = 0; i < Realms.Count; i++)
            {
                var r = Realms[i];
                Row(101 + i, r.Name, ip, r.Port, r.FlashName ?? r.Name, r.FlashIcon, r.FlashHint ?? "", 0);
            }

            Line("\t</table_data>");
            Line("</database>");
            Line("</mysqldump>");
            return sb.ToString();
        }

        static string Esc(string value) => SecurityElement.Escape(value) ?? "";
    }

    sealed record ServerGroup(string FlashName, string FlashIcon, string? FlashHint = null);
    sealed record ServerRealm(string Name, int Port, string? FlashName, string FlashIcon, string? FlashHint = null);

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

    sealed record ItemTypeEdit(uint Id, int Field, string Original, string Patched, string Reason)
    {
        public static (byte[]? Bytes, string? Error) ApplyAll(byte[] encrypted, List<ItemTypeEdit> edits)
        {
            var plain = ItemTypeCipher.Decrypt(encrypted);
            var lines = Encoding.Latin1.GetString(plain).Split("\r\n");
            var byId = new Dictionary<uint, int>();
            for (var i = 0; i < lines.Length; i++)
            {
                var sep = lines[i].IndexOf("@@", StringComparison.Ordinal);
                if (sep > 0 && uint.TryParse(lines[i].AsSpan(0, sep), out var id))
                    byId.TryAdd(id, i);
            }

            foreach (var edit in edits)
            {
                if (!byId.TryGetValue(edit.Id, out var index))
                    return (null, $"item {edit.Id} ({edit.Reason}): not found");
                var fields = lines[index].Split("@@");
                if (edit.Field < 0 || edit.Field >= fields.Length)
                    return (null, $"item {edit.Id} field {edit.Field} ({edit.Reason}): out of range");
                if (fields[edit.Field] != edit.Original)
                    return (null, $"item {edit.Id} field {edit.Field} ({edit.Reason}): expected {edit.Original}, found {fields[edit.Field]}");
                fields[edit.Field] = edit.Patched;
                lines[index] = string.Join("@@", fields);
            }

            return (ItemTypeCipher.Encrypt(Encoding.Latin1.GetBytes(string.Join("\r\n", lines))), null);
        }
    }

    sealed record FlashBitmap(string File, int Id, string Png, string Reason)
    {
        const int DefineBitsLossless2 = 36, End = 0;

        // SWF: "FWS" or "CWS" (zlib after the 8-byte header), version, file length. The body
        // opens with the frame rect (5-bit field size, then four fields), frame rate and
        // count, then tags: u16 code << 6 | length, length 63 meaning a u32 length follows.
        // DefineBitsLossless2 is id u16, format u8 (5 = 32-bit), width u16, height u16, then
        // zlib of premultiplied ARGB rows.
        public static (byte[]? Bytes, string? Error) ApplyAll(byte[] swf, List<FlashBitmap> swaps, string inputsDir)
        {
            if (swf.Length < 8 || swf[1] != 'W' || swf[2] != 'S' || swf[0] is not ((byte)'F' or (byte)'C'))
                return (null, "not a SWF file");
            byte[] body;
            if (swf[0] == 'C')
            {
                using var inflate = new ZLibStream(new MemoryStream(swf, 8, swf.Length - 8), CompressionMode.Decompress);
                var inflated = new MemoryStream();
                inflate.CopyTo(inflated);
                body = inflated.ToArray();
            }
            else
                body = swf[8..];

            var byId = swaps.ToDictionary(s => s.Id);
            var output = new MemoryStream();
            var at = ((5 + 4 * (body[0] >> 3) + 7) >> 3) + 4;
            output.Write(body, 0, at);
            var done = new HashSet<int>();
            while (at < body.Length)
            {
                int start = at, header = BitConverter.ToUInt16(body, at), code = header >> 6, length = header & 63;
                at += 2;
                if (length == 63)
                {
                    length = BitConverter.ToInt32(body, at);
                    at += 4;
                }
                var tag = body.AsSpan(at, length);
                at += length;
                if (code == DefineBitsLossless2 && byId.TryGetValue(BitConverter.ToUInt16(tag), out var swap))
                {
                    var (replaced, error) = swap.Replace(tag, inputsDir);
                    if (error != null)
                        return (null, $"bitmap {swap.Id} ({swap.Reason}): {error}");
                    output.Write(BitConverter.GetBytes((ushort)(DefineBitsLossless2 << 6 | 63)));
                    output.Write(BitConverter.GetBytes(replaced!.Length));
                    output.Write(replaced);
                    done.Add(swap.Id);
                }
                else
                    output.Write(body, start, at - start);
                if (code == End)
                    break;
            }
            if (swaps.FirstOrDefault(s => !done.Contains(s.Id)) is { } missing)
                return (null, $"bitmap {missing.Id} ({missing.Reason}): no such lossless bitmap");

            var result = new MemoryStream();
            result.Write(swf, 0, 4);
            result.Write(BitConverter.GetBytes((uint)(output.Length + 8)));
            if (swf[0] == 'C')
                using (var deflate = new ZLibStream(result, CompressionLevel.SmallestSize, leaveOpen: true))
                    output.WriteTo(deflate);
            else
                output.WriteTo(result);
            return (result.ToArray(), null);
        }

        (byte[]? Tag, string? Error) Replace(ReadOnlySpan<byte> tag, string inputsDir)
        {
            int format = tag[2], width = BitConverter.ToUInt16(tag[3..]), height = BitConverter.ToUInt16(tag[5..]);
            if (format != 5)
                return (null, $"expected a 32-bit bitmap, found format {format}");
            using var png = SkiaSharp.SKBitmap.Decode(Path.Combine(inputsDir, this.Png));
            if (png == null)
                return (null, $"can't read {this.Png}");
            if (png.Width != width || png.Height != height)
                return (null, $"{this.Png} is {png.Width}x{png.Height}, the client's bitmap is {width}x{height}");

            var argb = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var c = png.GetPixel(x, y);
                    var i = (y * width + x) * 4;
                    argb[i] = c.Alpha;
                    argb[i + 1] = (byte)(c.Red * c.Alpha / 255);
                    argb[i + 2] = (byte)(c.Green * c.Alpha / 255);
                    argb[i + 3] = (byte)(c.Blue * c.Alpha / 255);
                }
            var packed = new MemoryStream();
            packed.Write(tag[..7]);
            using (var deflate = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
                deflate.Write(argb);
            return (packed.ToArray(), null);
        }
    }

    static class ItemTypeCipher
    {
        static readonly byte[] Key = CreateKey(0x2537);

        public static byte[] Decrypt(byte[] data)
        {
            var result = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                var b = data[i] ^ Key[i % Key.Length];
                var shift = i % 8;
                result[i] = (byte)((b >> shift) | (b << (8 - shift)));
            }
            return result;
        }

        public static byte[] Encrypt(byte[] data)
        {
            var result = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                var shift = i % 8;
                int b = data[i];
                result[i] = (byte)(((b << shift) | (b >> (8 - shift))) ^ Key[i % Key.Length]);
            }
            return result;
        }

        static byte[] CreateKey(uint seed)
        {
            var key = new byte[128];
            for (var i = 0; i < key.Length; i++)
            {
                seed = seed * 214013 + 2531011;
                key[i] = (byte)((seed >> 16) & 0x7FFF);
            }
            return key;
        }
    }
}

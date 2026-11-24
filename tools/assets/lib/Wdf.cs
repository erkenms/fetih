using System.Buffers.Binary;
using System.Text;

// A TQ .wdf package (data.wdf, c3.wdf). Little-endian: "PFDW", uint32 file count, uint32 index
// offset; the index holds per file uint32 name hash, offset, size and reserved space. Files
// are stored whole, without compression, and found only by the hash of their path.
sealed class WdfPackage : IDisposable
{
    public readonly record struct Entry(uint Hash, uint Offset, uint Size);

    public string Path { get; }
    public IReadOnlyDictionary<uint, Entry> Entries => entries;
    private readonly Dictionary<uint, Entry> entries = new();
    private readonly FileStream stream;

    public WdfPackage(string path)
    {
        Path = path;
        stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x57444650)
            throw new InvalidDataException($"{path} isn't a WDF package");
        var count = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var index = new byte[count * 16];
        stream.Position = indexOffset;
        stream.ReadExactly(index);
        for (var i = 0; i < count; i++)
        {
            var e = index.AsSpan(i * 16);
            var entry = new Entry(
                BinaryPrimitives.ReadUInt32LittleEndian(e),
                BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[8..]));
            entries.TryAdd(entry.Hash, entry);
        }
    }

    public bool TryGet(string path, out Entry entry) => entries.TryGetValue(Hash(path), out entry);

    public byte[] Read(Entry entry)
    {
        var data = new byte[entry.Size];
        lock (stream)
        {
            stream.Position = entry.Offset;
            stream.ReadExactly(data);
        }
        return data;
    }

    public void Dispose() => stream.Dispose();

    // The client hashes the path lowercased, with '/' separators
    public static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();

    public static uint Hash(string path)
    {
        var bytes = Encoding.Latin1.GetBytes(Normalize(path));
        var m = new uint[70];
        Buffer.BlockCopy(bytes, 0, m, 0, Math.Min(bytes.Length, 256));
        var n = 0;
        while (n < 64 && m[n] != 0)
            n++;
        m[n++] = 0x9BE74448;
        m[n++] = 0x66F42C48;
        uint v = 0xF4FA8928, x = 0x37A8470E, y = 0x7758B42B;
        for (var i = 0; i < n; i++)
        {
            v = (v << 1) | (v >> 31);
            var w = 0x267B0B11 ^ v;
            x ^= m[i];
            y ^= m[i];

            ulong product = (ulong)x * (((w + y) | 0x2040801) & 0xBFEF7FDF);
            uint lo = (uint)product, hi = (uint)(product >> 32);
            ulong sum = (ulong)lo + hi + (hi != 0 ? 1u : 0u);
            var nextX = (uint)sum + (uint)(sum >> 32);

            product = (ulong)y * (((w + x) | 0x804021) & 0x7DFEFBFF);
            lo = (uint)product;
            hi = (uint)(product >> 32);
            ulong doubled = (ulong)hi + hi;
            sum = (ulong)lo + (uint)doubled + (uint)(doubled >> 32);
            var nextY = (uint)sum;
            if (sum >> 32 != 0)
                nextY += 2;

            x = nextX;
            y = nextY;
        }
        return x ^ y;
    }
}

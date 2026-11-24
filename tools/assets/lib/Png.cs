using System.Buffers.Binary;
using System.IO.Compression;
using StbImageSharp;

static class Images
{
    // Any picture the client uses: DDS, or JPEG, BMP, TGA and PNG. Some .dds files hold a TGA.
    public static Rgba Decode(byte[] data)
    {
        if (Dds.IsDds(data))
            return Dds.Decode(data);
        var image = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
        return new Rgba(image.Width, image.Height, image.Data);
    }

    // The four quarters of a picture, top left, top right, bottom left, bottom right
    public static Rgba Join(Rgba a, Rgba b, Rgba c, Rgba d)
    {
        int left = Math.Max(a.Width, c.Width), top = Math.Max(a.Height, b.Height);
        var joined = new Rgba(left + Math.Max(b.Width, d.Width), top + Math.Max(c.Height, d.Height),
            new byte[(left + Math.Max(b.Width, d.Width)) * (top + Math.Max(c.Height, d.Height)) * 4]);
        Copy(a, joined, 0, 0);
        Copy(b, joined, left, 0);
        Copy(c, joined, 0, top);
        Copy(d, joined, left, top);
        return joined;
    }

    private static void Copy(Rgba from, Rgba to, int x, int y)
    {
        for (var row = 0; row < from.Height; row++)
            from.Pixels.AsSpan(row * from.Width * 4, from.Width * 4)
                .CopyTo(to.Pixels.AsSpan(((y + row) * to.Width + x) * 4));
    }

    public static byte[] Png(Rgba image)
    {
        var stride = image.Width * 4;
        var raw = new byte[(stride + 1) * image.Height];
        var candidate = new byte[stride];
        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Pixels.AsSpan(y * stride, stride);
            var above = y > 0 ? image.Pixels.AsSpan((y - 1) * stride, stride) : default;
            // Per row, the filter whose output has the smallest sum of absolute values
            var best = long.MaxValue;
            for (byte filter = 0; filter < 5; filter++)
            {
                long cost = 0;
                for (var i = 0; i < stride; i++)
                {
                    int left = i >= 4 ? row[i - 4] : 0, up = y > 0 ? above[i] : 0, upLeft = i >= 4 && y > 0 ? above[i - 4] : 0;
                    var value = (byte)(row[i] - filter switch
                    {
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => Paeth(left, up, upLeft),
                        _ => 0,
                    });
                    candidate[i] = value;
                    cost += (sbyte)value < 0 ? -(sbyte)value : value;
                }
                if (cost < best)
                {
                    best = cost;
                    raw[y * (stride + 1)] = filter;
                    candidate.CopyTo(raw, y * (stride + 1) + 1);
                }
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~Crc(Crc(0xFFFFFFFF, typeBytes), data));
        png.Write(word);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

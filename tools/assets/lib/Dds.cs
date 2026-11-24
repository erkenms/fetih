using System.Buffers.Binary;

// A decoded image: RGBA, 4 bytes per pixel, rows top to bottom
sealed record Rgba(int Width, int Height, byte[] Pixels);

// DirectDraw Surfaces as the client stores them: DXT1, DXT3, DXT5, or uncompressed with
// bit masks (A8R8G8B8, X8R8G8B8, R8G8B8, R5G6B5, A1R5G5B5, A4R4G4B4). Only the top mip
// level is read. Header: "DDS ", then 124 bytes with height at 12, width at 16, pixel
// format at 76 (flags 80, FourCC 84, bit count 88, masks R/G/B/A 92..104).
static class Dds
{
    public static bool IsDds(ReadOnlySpan<byte> data) =>
        data.Length >= 128 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x20534444;

    public static Rgba Decode(ReadOnlySpan<byte> data)
    {
        if (!IsDds(data))
            throw new InvalidDataException("Not a DDS file");
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[80..]);
        var fourCC = BinaryPrimitives.ReadUInt32LittleEndian(data[84..]);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException($"Bad DDS size {width}x{height}");
        var body = data[128..];
        var pixels = new byte[width * height * 4];
        if ((flags & 4) != 0)
        {
            switch (fourCC)
            {
                case 0x31545844: Blocks(body, width, height, pixels, 8, Dxt1Block); break;
                case 0x33545844: Blocks(body, width, height, pixels, 16, Dxt3Block); break;
                case 0x35545844: Blocks(body, width, height, pixels, 16, Dxt5Block); break;
                default:
                    throw new InvalidDataException($"Unsupported DDS FourCC 0x{fourCC:X8}");
            }
        }
        else
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(data[88..]);
            var masks = new uint[4];
            for (var i = 0; i < 4; i++)
                masks[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(92 + i * 4)..]);
            if ((flags & 1) == 0)
                masks[3] = 0;
            Uncompressed(body, width, height, bits, masks, pixels);
        }
        return new Rgba(width, height, pixels);
    }

    private delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<byte> out64);

    private static void Blocks(ReadOnlySpan<byte> body, int width, int height, byte[] pixels, int blockSize, BlockDecoder decode)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        if (body.Length < blocksX * blocksY * blockSize)
            throw new InvalidDataException("DDS data ends early");
        Span<byte> tile = stackalloc byte[64];
        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            decode(body.Slice((by * blocksX + bx) * blockSize, blockSize), tile);
            for (var py = 0; py < 4; py++)
            {
                var y = by * 4 + py;
                if (y >= height)
                    break;
                for (var px = 0; px < 4; px++)
                {
                    var x = bx * 4 + px;
                    if (x >= width)
                        break;
                    tile.Slice((py * 4 + px) * 4, 4).CopyTo(pixels.AsSpan((y * width + x) * 4));
                }
            }
        }
    }

    // Fills the 4 colours of a colour block; DXT1 with c0 <= c1 has 3 colours and transparent black
    private static void Palette(ReadOnlySpan<byte> block, Span<byte> colors, bool dxt1)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        Rgb565(c0, colors);
        Rgb565(c1, colors[4..]);
        for (var i = 0; i < 3; i++)
        {
            int a = colors[i], b = colors[4 + i];
            if (!dxt1 || c0 > c1)
            {
                colors[8 + i] = (byte)((2 * a + b) / 3);
                colors[12 + i] = (byte)((a + 2 * b) / 3);
            }
            else
            {
                colors[8 + i] = (byte)((a + b) / 2);
                colors[12 + i] = 0;
            }
        }
        colors[3] = colors[7] = colors[11] = 255;
        colors[15] = (byte)(!dxt1 || c0 > c1 ? 255 : 0);
    }

    private static void ColorIndices(ReadOnlySpan<byte> block, ReadOnlySpan<byte> colors, Span<byte> tile)
    {
        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        for (var i = 0; i < 16; i++)
            colors.Slice((int)((indices >> (2 * i)) & 3) * 4, 4).CopyTo(tile[(i * 4)..]);
    }

    private static void Dxt1Block(ReadOnlySpan<byte> block, Span<byte> tile)
    {
        Span<byte> colors = stackalloc byte[16];
        Palette(block, colors, dxt1: true);
        ColorIndices(block, colors, tile);
    }

    private static void Dxt3Block(ReadOnlySpan<byte> block, Span<byte> tile)
    {
        Span<byte> colors = stackalloc byte[16];
        Palette(block[8..], colors, dxt1: false);
        ColorIndices(block[8..], colors, tile);
        var alpha = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (var i = 0; i < 16; i++)
            tile[i * 4 + 3] = (byte)(((alpha >> (4 * i)) & 0xF) * 17);
    }

    private static void Dxt5Block(ReadOnlySpan<byte> block, Span<byte> tile)
    {
        Span<byte> colors = stackalloc byte[16];
        Palette(block[8..], colors, dxt1: false);
        ColorIndices(block[8..], colors, tile);
        int a0 = block[0], a1 = block[1];
        Span<byte> alphas = stackalloc byte[8];
        alphas[0] = (byte)a0;
        alphas[1] = (byte)a1;
        if (a0 > a1)
            for (var i = 1; i < 7; i++)
                alphas[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
        else
        {
            for (var i = 1; i < 5; i++)
                alphas[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            alphas[6] = 0;
            alphas[7] = 255;
        }
        ulong bits = 0;
        for (var i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (8 * i);
        for (var i = 0; i < 16; i++)
            tile[i * 4 + 3] = alphas[(int)((bits >> (3 * i)) & 7)];
    }

    private static void Rgb565(ushort c, Span<byte> rgba)
    {
        rgba[0] = (byte)(((c >> 11) & 31) * 255 / 31);
        rgba[1] = (byte)(((c >> 5) & 63) * 255 / 63);
        rgba[2] = (byte)((c & 31) * 255 / 31);
        rgba[3] = 255;
    }

    private static void Uncompressed(ReadOnlySpan<byte> body, int width, int height, int bits, uint[] masks, byte[] pixels)
    {
        if (bits is not (16 or 24 or 32))
            throw new InvalidDataException($"Unsupported DDS bit count {bits}");
        var bytes = bits / 8;
        var pitch = width * bytes;
        if (body.Length < pitch * height)
            throw new InvalidDataException("DDS data ends early");
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var p = body.Slice(y * pitch + x * bytes, bytes);
            uint value = bytes switch
            {
                2 => BinaryPrimitives.ReadUInt16LittleEndian(p),
                3 => (uint)(p[0] | p[1] << 8 | p[2] << 16),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(p),
            };
            var o = (y * width + x) * 4;
            for (var c = 0; c < 4; c++)
                pixels[o + c] = masks[c] == 0 ? (byte)255 : Channel(value, masks[c]);
        }
    }

    private static byte Channel(uint value, uint mask)
    {
        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        return (byte)(((value & mask) >> shift) * 255 / max);
    }
}

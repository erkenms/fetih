using System.Reflection;

namespace Fetih.Launcher;

// The old Play screen's art (Play/Main.bmp, Main.rgn, Button/), embedded by the build; a plain
// window when it wasn't
static unsafe class Art
{
    public static byte[]? Bytes(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null)
            return null;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    // The parchment's green is pure; the buttons' edges blend into it
    public static Picture? Parchment() => Bytes("Main.bmp") is { } bmp ? Picture.FromBmp(bmp, (r, g, b) => g > 200 && r < 80 && b < 80) : null;

    // The character over the right pane, as PlayGui.ini's fg_pic0 (Play/MainS.bmp at 594,0)
    public static Picture? Character() => Button("MainS.bmp");

    public static Picture? Plain(string name) => Bytes(name) is { } bmp ? Picture.FromBmp(bmp, (_, _, _) => false) : null;

    public static Picture? Button(string name) =>
        Bytes(name) is { } bmp ? Picture.FromBmp(bmp, (r, g, b) => g > 100 && g - Math.Max(r, b) > 50) : null;

    // Main.rgn: u32 length of the rest, then an RGNDATA blob as GetRegionData wrote it
    public static nint Region(string name, float scale)
    {
        if (Bytes(name) is not { Length: > 36 } rgn || BitConverter.ToInt32(rgn, 0) != rgn.Length - 4)
            return 0;
        var xform = stackalloc float[6] { scale, 0, 0, scale, 0, 0 };
        fixed (byte* p = rgn)
            return Native.ExtCreateRegion((nint)xform, (uint)rgn.Length - 4, p + 4);
    }
}

// Premultiplied BGRA, scaled once to the screen's DPI and drawn 1:1 with AlphaBlend
sealed unsafe class Picture(int width, int height, uint[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    readonly uint[] _pixels = pixels;

    public static Picture? FromBmp(byte[] bmp, Func<int, int, int, bool> isKey)
    {
        var offset = BitConverter.ToInt32(bmp, 10);
        var width = BitConverter.ToInt32(bmp, 18);
        var height = BitConverter.ToInt32(bmp, 22);
        if (BitConverter.ToInt16(bmp, 28) != 24)
            return null;
        var bottomUp = height > 0;
        height = Math.Abs(height);
        var stride = (width * 3 + 3) & ~3;
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = offset + (bottomUp ? height - 1 - y : y) * stride;
            for (var x = 0; x < width; x++)
            {
                int b = bmp[row + x * 3], g = bmp[row + x * 3 + 1], r = bmp[row + x * 3 + 2];
                pixels[y * width + x] = isKey(r, g, b) ? 0 : 0xFF000000u | (uint)(r << 16 | g << 8 | b);
            }
        }
        return new Picture(width, height, pixels);
    }

    public Picture Copy() => new(this.Width, this.Height, (uint[])_pixels.Clone());

    // Copies a w×h block from (sx, sy) to (dx, dy) within this picture
    public void CopyBlock(int sx, int sy, int w, int h, int dx, int dy)
    {
        for (var y = 0; y < h; y++)
            Array.Copy(_pixels, (sy + y) * this.Width + sx, _pixels, (dy + y) * this.Width + dx, w);
    }

    public Picture Scale(double scale)
    {
        if (Math.Abs(scale - 1) < 0.001)
            return this;
        var w = Math.Max(1, (int)Math.Round(this.Width * scale));
        var h = Math.Max(1, (int)Math.Round(this.Height * scale));
        var result = new uint[w * h];
        for (var y = 0; y < h; y++)
        {
            var fy = Math.Clamp((y + 0.5) / scale - 0.5, 0, this.Height - 1);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, this.Height - 1);
            var ty = fy - y0;
            for (var x = 0; x < w; x++)
            {
                var fx = Math.Clamp((x + 0.5) / scale - 0.5, 0, this.Width - 1);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, this.Width - 1);
                var tx = fx - x0;
                uint p00 = _pixels[y0 * this.Width + x0], p01 = _pixels[y0 * this.Width + x1];
                uint p10 = _pixels[y1 * this.Width + x0], p11 = _pixels[y1 * this.Width + x1];
                uint value = 0;
                for (var shift = 0; shift < 32; shift += 8)
                {
                    double Channel(uint p) => (p >> shift) & 0xFF;
                    var top = Channel(p00) + (Channel(p01) - Channel(p00)) * tx;
                    var bottom = Channel(p10) + (Channel(p11) - Channel(p10)) * tx;
                    value |= (uint)Math.Clamp((int)Math.Round(top + (bottom - top) * ty), 0, 255) << shift;
                }
                result[y * w + x] = value;
            }
        }
        return new Picture(w, h, result);
    }

    public nint ToBitmap()
    {
        var header = new Native.BITMAPINFOHEADER
        {
            Size = 40,
            Width = this.Width,
            Height = -this.Height,
            Planes = 1,
            BitCount = 32,
        };
        void* bits;
        var bitmap = Native.CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap != 0)
            fixed (uint* source = _pixels)
                Buffer.MemoryCopy(source, bits, _pixels.Length * 4L, _pixels.Length * 4L);
        return bitmap;
    }
}

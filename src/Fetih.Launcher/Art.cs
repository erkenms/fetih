using System.Reflection;

namespace Fetih.Launcher;

// The old Play screen's art (Play/Main.bmp, Main.rgn, Button/), embedded by the build; a plain
// window when it wasn't
static unsafe class Art
{
    public const uint Key = 0x0000FF00; // the green cut away around the parchment and buttons

    public static byte[]? Bytes(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null)
            return null;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    // A 24-bit .bmp as a GDI bitmap
    public static nint Bitmap(string name)
    {
        if (Bytes(name) is not { } bmp)
            return 0;
        var screen = Native.GetDC(0);
        try
        {
            fixed (byte* p = bmp)
            {
                var bits = p + BitConverter.ToInt32(bmp, 10);
                // CBM_INIT, DIB_RGB_COLORS; the BITMAPINFOHEADER follows the 14-byte file header
                return Native.CreateDIBitmap(screen, p + 14, 4, bits, p + 14, 0);
            }
        }
        finally
        {
            Native.ReleaseDC(0, screen);
        }
    }

    // Main.rgn: u32 length of the rest, then an RGNDATA blob as GetRegionData wrote it
    public static nint Region(string name)
    {
        if (Bytes(name) is not { Length: > 36 } rgn || BitConverter.ToInt32(rgn, 0) != rgn.Length - 4)
            return 0;
        fixed (byte* p = rgn)
            return Native.ExtCreateRegion(0, (uint)rgn.Length - 4, p + 4);
    }
}

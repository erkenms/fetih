namespace Fetih.Web.Features.Maps;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Fetih.Game.World;
using SharpCompress.Archives.SevenZip;
using SkiaSharp;
using StbImageSharp;

// Renders Conquest Online map tiles (ground and covers) directly at any zoom level using SkiaSharp.
// Shared between tools/build.cs (low-zoom prerender) and Fetih.Web (on-demand high-zoom tiles).
public sealed class MapTileRenderer(Func<string, byte[]?> readFile)
{
    public const int TileSize = 256;

    public static Encoding Gbk { get; } = CreateGbk();

    private readonly ConcurrentDictionary<string, Dictionary<string, List<string>>?> anis = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<TileSceneSprite>?> scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SKImage?> images = new(StringComparer.OrdinalIgnoreCase);

    public RenderedTileMap? LoadMap(string mapFile, uint puzzleSize)
    {
        var data = ReadDMap(readFile, mapFile);
        if (data == null)
            return null;
        var drawing = ParseDMap(data, Scene);
        var puzzleBytes = readFile(drawing.Puzzle) ?? throw new InvalidDataException($"No {drawing.Puzzle}");
        var puzzle = ParsePuzzle(puzzleBytes);
        int grid = (int)puzzleSize;
        int width = puzzle.Width * grid;
        int height = puzzle.Height * grid;
        int bgX = 32 * drawing.Width - width / 2;
        int bgY = 16 * drawing.Height - height / 2 + (drawing.Height % 2 == 1 ? 16 : 0);
        int maxZoom = 0;
        while ((TileSize << maxZoom) < Math.Max(width, height))
            maxZoom++;

        var puzzleFrames = Ani(puzzle.Ani);
        var puzzleImages = new Dictionary<ushort, SKImage>();
        if (puzzleFrames != null)
        {
            foreach (var n in puzzle.Tiles)
            {
                if (n == 0xFFFF || puzzleImages.ContainsKey(n))
                    continue;
                if (puzzleFrames.GetValueOrDefault($"Puzzle{n}") is [var frame, ..] && DecodeImage(frame) is { } img)
                    puzzleImages[n] = img;
            }
        }

        var groundSprites = PlaceSprites(drawing.Ground, drawing, bgX, bgY);
        var coverSprites = PlaceSprites(drawing.Covers, drawing, bgX, bgY);

        return new RenderedTileMap(width, height, maxZoom, grid, puzzle, puzzleImages, groundSprites, coverSprites);
    }

    private List<PlacedSprite> PlaceSprites(List<TileMapSprite> list, TileMapDrawing drawing, int bgX, int bgY)
    {
        var placed = new List<PlacedSprite>();
        foreach (var sprite in list.OrderBy(s => s.CellX + s.CellY).ThenBy(s => s.CellX))
        {
            var frames = Ani(sprite.Ani)?.GetValueOrDefault(sprite.Title);
            if (frames is [var mask, ..] && mask.EndsWith(".msk", StringComparison.OrdinalIgnoreCase))
                continue;
            if (frames is not [var frame, ..])
                continue;
            if (frames.Count > 1 && sprite.Interval > 0)
                continue;
            if (DecodeImage(frame) is not { } img)
                continue;
            int x = 32 * (sprite.CellX - sprite.CellY) + 32 * drawing.Width + sprite.OffsetX - bgX;
            int y = 16 * (sprite.CellX + sprite.CellY) + 16 + sprite.OffsetY - bgY;
            placed.Add(new PlacedSprite(img, x, y));
        }
        return placed;
    }

    private Dictionary<string, List<string>>? Ani(string path) =>
        anis.GetOrAdd(path, p => readFile(p) is { } data ? ParseAni(data) : null);

    private List<TileSceneSprite>? Scene(string path) =>
        scenes.GetOrAdd(path, p => readFile(p) is { } data ? ParseScene(data) : null);

    private SKImage? DecodeImage(string path)
    {
        if (path.Contains("/puzzle/", StringComparison.OrdinalIgnoreCase))
            return DecodeUncached(path);
        return images.GetOrAdd(path, DecodeUncached);
    }

    private SKImage? DecodeUncached(string path)
    {
        try
        {
            var data = readFile(path);
            return data == null ? null : DecodeToSkImage(data);
        }
        catch (Exception e) when (e is not IOException)
        {
            return null;
        }
    }

    public static SKImage? DecodeToSkImage(ReadOnlySpan<byte> data)
    {
        byte[] rgba;
        int width, height;
        if (IsDds(data))
        {
            (width, height, rgba) = DecodeDds(data);
        }
        else
        {
            var img = ImageResult.FromMemory(data.ToArray(), ColorComponents.RedGreenBlueAlpha);
            width = img.Width;
            height = img.Height;
            rgba = img.Data;
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    public static byte[]? ReadDMap(Func<string, byte[]?> readFile, string path)
    {
        if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && readFile(path) is { } packed)
        {
            using var archive = SevenZipArchive.OpenArchive(new MemoryStream(packed));
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory &&
                e.Key!.EndsWith(".dmap", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;
            using var stream = entry.OpenEntryStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        return readFile(Path.ChangeExtension(path, ".DMap"));
    }

    public static Dictionary<string, List<string>> ParseAni(byte[] data)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? frames = null;
        foreach (var raw in Gbk.GetString(data).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                if (end > 1)
                    sections[line[1..end].Trim()] = frames = new();
                continue;
            }
            if (frames != null && line.StartsWith("Frame", StringComparison.OrdinalIgnoreCase)
                && line.IndexOf('=') is int eq && eq > 5 && int.TryParse(line[5..eq], out _))
                frames.Add(line[(eq + 1)..].Trim().Replace('\\', '/'));
        }
        return sections;
    }

    private static TilePuzzle ParsePuzzle(ReadOnlySpan<byte> data)
    {
        if (data.Length < 272 || !data[..6].SequenceEqual("PUZZLE"u8))
            throw new InvalidDataException("Not a puzzle file");
        var ani = ReadText(data.Slice(8, 256));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[264..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[268..]);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || data.Length < 272 + width * height * 2)
            throw new InvalidDataException($"Puzzle has a bad size {width}x{height}");
        var tiles = new ushort[width * height];
        for (var i = 0; i < tiles.Length; i++)
            tiles[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(272 + i * 2)..]);
        return new TilePuzzle(ani, width, height, tiles);
    }

    private static TileMapDrawing ParseDMap(ReadOnlySpan<byte> data, Func<string, List<TileSceneSprite>?> scene)
    {
        var puzzle = ReadText(data.Slice(8, 260));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[268..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[272..]);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidDataException($"DMap has a bad size {width}x{height}");
        var offset = 276 + height * (width * 6 + 4);
        var portals = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4 + portals * 12;
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4;

        var ground = new List<TileMapSprite>();
        var covers = new List<TileMapSprite>();
        for (var i = 0; i < count; i++)
        {
            var type = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            var o = data[(offset + 4)..];
            switch (type)
            {
                case 1:
                    int x = BinaryPrimitives.ReadInt32LittleEndian(o[260..]), y = BinaryPrimitives.ReadInt32LittleEndian(o[264..]);
                    foreach (var part in scene(ReadText(o[..260])) ?? [])
                        ground.Add(new TileMapSprite(part.Ani, part.Title, x + part.CellX, y + part.CellY, part.PixelX, part.PixelY, part.Interval));
                    break;
                case 4:
                    covers.Add(new TileMapSprite(ReadText(o[..260]), ReadText(o.Slice(260, 128)),
                        BinaryPrimitives.ReadInt32LittleEndian(o[388..]), BinaryPrimitives.ReadInt32LittleEndian(o[392..]),
                        -BinaryPrimitives.ReadInt32LittleEndian(o[404..]), -BinaryPrimitives.ReadInt32LittleEndian(o[408..]),
                        BinaryPrimitives.ReadInt32LittleEndian(o[412..])));
                    break;
            }
            offset += type switch
            {
                1 => 272,
                4 => 420,
                10 => 76,
                15 => 280,
                _ => throw new InvalidDataException($"DMap object {i} has an unknown type {type}"),
            };
        }
        return new TileMapDrawing(puzzle, width, height, ground, covers);
    }

    private static List<TileSceneSprite> ParseScene(ReadOnlySpan<byte> data)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        var parts = new List<TileSceneSprite>();
        var offset = 4;
        for (var p = 0; p < count; p++)
        {
            var h = data[offset..];
            int width = BinaryPrimitives.ReadInt32LittleEndian(h[332..]), height = BinaryPrimitives.ReadInt32LittleEndian(h[336..]);
            parts.Add(new TileSceneSprite(ReadText(h[..256]), ReadText(h.Slice(256, 64)),
                BinaryPrimitives.ReadInt32LittleEndian(h[320..]), BinaryPrimitives.ReadInt32LittleEndian(h[324..]),
                BinaryPrimitives.ReadInt32LittleEndian(h[344..]), BinaryPrimitives.ReadInt32LittleEndian(h[348..]),
                BinaryPrimitives.ReadInt32LittleEndian(h[328..])));
            offset += 356 + width * height * 12;
        }
        return parts;
    }

    private static string ReadText(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]).Replace('\\', '/').Trim();
    }

    private static Encoding CreateGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    private static bool IsDds(ReadOnlySpan<byte> data) =>
        data.Length >= 128 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x20534444;

    private static (int Width, int Height, byte[] Rgba) DecodeDds(ReadOnlySpan<byte> data)
    {
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
                case 0x31545844: DecodeBlocks(body, width, height, pixels, 8, Dxt1Block); break;
                case 0x33545844: DecodeBlocks(body, width, height, pixels, 16, Dxt3Block); break;
                case 0x35545844: DecodeBlocks(body, width, height, pixels, 16, Dxt5Block); break;
                default: throw new InvalidDataException($"Unsupported DDS FourCC 0x{fourCC:X8}");
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
            UncompressedDds(body, width, height, bits, masks, pixels);
        }
        return (width, height, pixels);
    }

    private delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<byte> out64);

    private static void DecodeBlocks(ReadOnlySpan<byte> body, int width, int height, byte[] pixels, int blockSize, BlockDecoder decode)
    {
        int blocksX = (width + 3) / 4, blocksY = (height + 3) / 4;
        if (body.Length < blocksX * blocksY * blockSize)
            throw new InvalidDataException("DDS data ends early");
        Span<byte> tile = stackalloc byte[64];
        for (int by = 0; by < blocksY; by++)
        for (int bx = 0; bx < blocksX; bx++)
        {
            decode(body.Slice((by * blocksX + bx) * blockSize, blockSize), tile);
            for (int row = 0; row < 4 && by * 4 + row < height; row++)
            {
                int cols = Math.Min(4, width - bx * 4);
                tile.Slice(row * 16, cols * 4).CopyTo(pixels.AsSpan(((by * 4 + row) * width + bx * 4) * 4));
            }
        }
    }

    private static void Dxt1Block(ReadOnlySpan<byte> b, Span<byte> o)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(b), c1 = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        Span<byte> p = stackalloc byte[16];
        Rgb565(c0, p);
        Rgb565(c1, p[4..]);
        if (c0 > c1)
        {
            for (int c = 0; c < 3; c++)
            {
                p[8 + c] = (byte)((2 * p[c] + p[4 + c]) / 3);
                p[12 + c] = (byte)((p[c] + 2 * p[4 + c]) / 3);
            }
            p[11] = p[15] = 255;
        }
        else
        {
            for (int c = 0; c < 3; c++)
                p[8 + c] = (byte)((p[c] + p[4 + c]) / 2);
            p[11] = 255;
            p[15] = 0;
        }
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        for (int i = 0; i < 16; i++)
            p.Slice((int)((bits >> (i * 2)) & 3) * 4, 4).CopyTo(o[(i * 4)..]);
    }

    private static void Dxt3Block(ReadOnlySpan<byte> b, Span<byte> o)
    {
        Dxt1Color(b[8..], o);
        ulong alpha = BinaryPrimitives.ReadUInt64LittleEndian(b);
        for (int i = 0; i < 16; i++)
            o[i * 4 + 3] = (byte)(((alpha >> (i * 4)) & 0xF) * 17);
    }

    private static void Dxt5Block(ReadOnlySpan<byte> b, Span<byte> o)
    {
        Dxt1Color(b[8..], o);
        Span<byte> a = stackalloc byte[8];
        a[0] = b[0];
        a[1] = b[1];
        if (a[0] > a[1])
            for (int i = 1; i <= 6; i++)
                a[i + 1] = (byte)(((7 - i) * a[0] + i * a[1]) / 7);
        else
        {
            for (int i = 1; i <= 4; i++)
                a[i + 1] = (byte)(((5 - i) * a[0] + i * a[1]) / 5);
            a[6] = 0;
            a[7] = 255;
        }
        ulong bits = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(b[2..]) | ((ulong)BinaryPrimitives.ReadUInt16LittleEndian(b[6..]) << 32);
        for (int i = 0; i < 16; i++)
            o[i * 4 + 3] = a[(int)((bits >> (i * 3)) & 7)];
    }

    private static void Dxt1Color(ReadOnlySpan<byte> b, Span<byte> o)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(b), c1 = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        Span<byte> p = stackalloc byte[16];
        Rgb565(c0, p);
        Rgb565(c1, p[4..]);
        for (int c = 0; c < 3; c++)
        {
            p[8 + c] = (byte)((2 * p[c] + p[4 + c]) / 3);
            p[12 + c] = (byte)((p[c] + 2 * p[4 + c]) / 3);
        }
        p[11] = p[15] = 255;
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        for (int i = 0; i < 16; i++)
            p.Slice((int)((bits >> (i * 2)) & 3) * 4, 4).CopyTo(o[(i * 4)..]);
    }

    private static void Rgb565(ushort v, Span<byte> o)
    {
        int r = (v >> 11) & 31, g = (v >> 5) & 63, b = v & 31;
        o[0] = (byte)((r << 3) | (r >> 2));
        o[1] = (byte)((g << 2) | (g >> 4));
        o[2] = (byte)((b << 3) | (b >> 2));
        o[3] = 255;
    }

    private static void UncompressedDds(ReadOnlySpan<byte> body, int width, int height, int bits, uint[] masks, byte[] pixels)
    {
        int stride = bits / 8;
        if (stride < 2 || stride > 4 || body.Length < width * height * stride)
            throw new InvalidDataException($"Unsupported {bits}-bit DDS");
        for (int i = 0; i < width * height; i++)
        {
            uint pixel = stride switch
            {
                2 => BinaryPrimitives.ReadUInt16LittleEndian(body[(i * 2)..]),
                3 => (uint)(body[i * 3] | (body[i * 3 + 1] << 8) | (body[i * 3 + 2] << 16)),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(body[(i * 4)..]),
            };
            pixels[i * 4] = Channel(pixel, masks[0]);
            pixels[i * 4 + 1] = Channel(pixel, masks[1]);
            pixels[i * 4 + 2] = Channel(pixel, masks[2]);
            pixels[i * 4 + 3] = masks[3] != 0 ? Channel(pixel, masks[3]) : (byte)255;
        }
    }

    private static byte Channel(uint pixel, uint mask)
    {
        if (mask == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift;
        return (byte)(((pixel & mask) >> shift) * 255 / max);
    }

    private sealed record TileMapSprite(string Ani, string Title, int CellX, int CellY, int OffsetX, int OffsetY, int Interval);
    private sealed record TileSceneSprite(string Ani, string Title, int PixelX, int PixelY, int CellX, int CellY, int Interval);
    private sealed record TileMapDrawing(string Puzzle, int Width, int Height, List<TileMapSprite> Ground, List<TileMapSprite> Covers);
    public sealed record TilePuzzle(string Ani, int Width, int Height, ushort[] Tiles);
    public sealed record PlacedSprite(SKImage Image, int X, int Y);
}

public sealed class RenderedTileMap(
    int width,
    int height,
    int maxZoom,
    int gridSize,
    MapTileRenderer.TilePuzzle puzzle,
    Dictionary<ushort, SKImage> puzzleImages,
    List<MapTileRenderer.PlacedSprite> groundSprites,
    List<MapTileRenderer.PlacedSprite> coverSprites)
{
    public int Width => width;
    public int Height => height;
    public int MaxZoom => maxZoom;
    public int GridSize => gridSize;

    public bool IsValidCoord(int z, int x, int y)
    {
        if (z < 0 || z > maxZoom || x < 0 || y < 0)
            return false;
        long scale = 1L << (maxZoom - z);
        return (long)x * MapTileRenderer.TileSize * scale < width
            && (long)y * MapTileRenderer.TileSize * scale < height;
    }

    public byte[]? RenderTile(string layer, int z, int x, int y)
    {
        if (!IsValidCoord(z, x, y))
            return null;

        int scale = 1 << (maxZoom - z);
        long left = (long)x * MapTileRenderer.TileSize * scale;
        long top = (long)y * MapTileRenderer.TileSize * scale;
        long right = left + (long)MapTileRenderer.TileSize * scale;
        long bottom = top + (long)MapTileRenderer.TileSize * scale;

        var info = new SKImageInfo(MapTileRenderer.TileSize, MapTileRenderer.TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(1f / scale, 1f / scale);
        canvas.Translate(-left, -top);

        var sampling = scale > 1 ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : SKSamplingOptions.Default;

        if (layer == "ground")
        {
            int tx0 = (int)Math.Max(0, left / gridSize);
            int tx1 = (int)Math.Min(puzzle.Width - 1, (right - 1) / gridSize);
            int ty0 = (int)Math.Max(0, top / gridSize);
            int ty1 = (int)Math.Min(puzzle.Height - 1, (bottom - 1) / gridSize);
            for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                var n = puzzle.Tiles[ty * puzzle.Width + tx];
                if (n != 0xFFFF && puzzleImages.TryGetValue(n, out var img))
                    canvas.DrawImage(img, tx * gridSize, ty * gridSize, sampling);
            }

            foreach (var s in groundSprites)
                if (s.X < right && s.X + s.Image.Width > left && s.Y < bottom && s.Y + s.Image.Height > top)
                    canvas.DrawImage(s.Image, s.X, s.Y, sampling);
        }
        else if (layer == "covers")
        {
            foreach (var s in coverSprites)
                if (s.X < right && s.X + s.Image.Width > left && s.Y < bottom && s.Y + s.Image.Height > top)
                    canvas.DrawImage(s.Image, s.X, s.Y, sampling);
        }

        canvas.Restore();

        using var pixmap = surface.PeekPixels();
        var span = pixmap.GetPixelSpan();
        bool visible = false;
        for (int i = 3; i < span.Length; i += 4)
        {
            if (span[i] != 0)
            {
                visible = true;
                break;
            }
        }
        if (!visible)
            return null;

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Webp, 82);
        return encoded.ToArray();
    }
}

// Loose + WDF (.wdf) client file reader for Fetih.Web's on-demand tile renderer.
public sealed class TileWdfArchive : IDisposable
{
    private readonly Dictionary<string, string> loose = new(StringComparer.Ordinal);
    private readonly List<Wdf> packages = [];

    public TileWdfArchive(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            loose.TryAdd(Normalize(Path.GetRelativePath(root, path)), path);
        foreach (var name in new[] { "data.wdf", "c3.wdf" })
            if (loose.TryGetValue(name, out var path))
                packages.Add(new Wdf(path));
    }

    public byte[]? Read(string path)
    {
        var key = Normalize(path);
        if (loose.TryGetValue(key, out var full))
            return File.ReadAllBytes(full);
        foreach (var pkg in packages)
            if (pkg.TryRead(key, out var data))
                return data;
        return null;
    }

    public void Dispose()
    {
        foreach (var pkg in packages)
            pkg.Dispose();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();

    private sealed class Wdf : IDisposable
    {
        private readonly Dictionary<uint, (uint Offset, uint Size)> entries = new();
        private readonly FileStream stream;

        public Wdf(string path)
        {
            stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x57444650)
                throw new InvalidDataException($"{path} isn't a WDF package");
            int count = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            uint indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            var index = new byte[count * 16];
            stream.Position = indexOffset;
            stream.ReadExactly(index);
            for (int i = 0; i < count; i++)
            {
                var e = index.AsSpan(i * 16);
                entries.TryAdd(
                    BinaryPrimitives.ReadUInt32LittleEndian(e),
                    (BinaryPrimitives.ReadUInt32LittleEndian(e[4..]), BinaryPrimitives.ReadUInt32LittleEndian(e[8..])));
            }
        }

        public bool TryRead(string normalizedPath, out byte[] data)
        {
            if (!entries.TryGetValue(Hash(normalizedPath), out var entry))
            {
                data = [];
                return false;
            }
            data = new byte[entry.Size];
            lock (stream)
            {
                stream.Position = entry.Offset;
                stream.ReadExactly(data);
            }
            return true;
        }

        public void Dispose() => stream.Dispose();

        private static uint Hash(string normalizedPath)
        {
            var bytes = Encoding.Latin1.GetBytes(normalizedPath);
            var m = new uint[70];
            Buffer.BlockCopy(bytes, 0, m, 0, Math.Min(bytes.Length, 256));
            int n = 0;
            while (n < 64 && m[n] != 0) n++;
            m[n++] = 0x9BE74448;
            m[n++] = 0x66F42C48;
            uint v = 0xF4FA8928, x = 0x37A8470E, y = 0x7758B42B;
            for (int i = 0; i < n; i++)
            {
                v = (v << 1) | (v >> 31);
                uint w = 0x267B0B11 ^ v;
                x ^= m[i];
                y ^= m[i];
                ulong product = (ulong)x * (((w + y) | 0x2040801) & 0xBFEF7FDF);
                uint lo = (uint)product, hi = (uint)(product >> 32);
                ulong sum = (ulong)lo + hi + (hi != 0 ? 1u : 0u);
                uint nextX = (uint)sum + (uint)(sum >> 32);
                product = (ulong)y * (((w + x) | 0x804021) & 0x7DFEFBFF);
                lo = (uint)product;
                hi = (uint)(product >> 32);
                ulong doubled = (ulong)hi + hi;
                sum = (ulong)lo + (uint)doubled + (uint)(doubled >> 32);
                uint nextY = (uint)sum;
                if (sum >> 32 != 0) nextY += 2;
                x = nextX;
                y = nextY;
            }
            return x ^ y;
        }
    }
}

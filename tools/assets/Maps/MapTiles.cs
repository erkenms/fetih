using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fetih.Game.World;
using SkiaSharp;

// Renders each map the client has into two Leaflet tile pyramids: the ground (puzzle
// background and terrain scene parts) and the covers, transparent, which the viewer can hide.
// Maps that share a DMap share their tiles, under maps/tiles/<DMap name>/; maps/<id>/meta.json
// says where they are and how a cell lands on them.
//
// A cell's world pixel is wx = 32·(x − y) + 32·mapWidth, wy = 16·(x + y) + 16 (the client's
// Cell2World). The background's top left sits at world (32·mapWidth − bgWidth/2,
// 16·mapHeight − bgHeight/2), 16 lower when mapHeight is odd, and the tiles are in background
// pixels. The background is width × height puzzle tiles of the GameMap.dat puzzle size.
//
// Sprites with several frames (fires, orbs, dragonflies) aren't in the pyramids: their frames
// go to anim/<n>/<frame>.png beside them, listed in animations.json and meta.json, and the
// viewer loops them over the tiles.
sealed class MapTiles(ClientFiles files, Output output)
{
    // Raise it when the rendering changes, so every map is rendered again
    public const string Version = "2";
    public const int TileSize = 256;

    public List<string> Summary { get; } = new();
    public ConcurrentBag<string> Errors { get; } = new();

    private readonly ConcurrentDictionary<string, Dictionary<string, List<string>>?> anis = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<SceneSprite>?> scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Rgba?> sprites = new(StringComparer.OrdinalIgnoreCase);

    // Renders the maps whose sources changed. With `only`, renders just those map IDs and
    // keeps what earlier runs wrote for the others.
    public void Build(IReadOnlySet<uint>? only)
    {
        var index = GameMapIndex.Parse(files.Read("ini/GameMap.dat") ?? throw new FileNotFoundException("No ini/GameMap.dat"));
        var tilesRoot = Path.Combine(output.Root, "maps", "tiles");
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int rendered = 0, unchanged = 0, maps = 0;

        foreach (var group in index.Values.GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Min(m => m.Id)))
        {
            var key = Path.GetFileNameWithoutExtension(group.Key).ToLowerInvariant();
            kept.Add(key);
            if (only != null && !group.Any(m => only.Contains(m.Id)))
            {
                foreach (var map in group)
                    KeepMeta(map.Id);
                continue;
            }
            try
            {
                var meta = Render(group.Key, group.First().PuzzleSize, key, Path.Combine(tilesRoot, key), out var fresh);
                if (meta == null)
                    continue;
                if (fresh) rendered++; else unchanged++;
                foreach (var map in group)
                {
                    output.Write($"maps/{map.Id}/meta.json", meta);
                    maps++;
                }
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                Errors.Add($"{group.Key}: {e.Message}");
            }
        }

        if (only == null && Directory.Exists(tilesRoot))
            foreach (var folder in Directory.EnumerateDirectories(tilesRoot).Where(f => !kept.Contains(Path.GetFileName(f))))
                Directory.Delete(folder, true);
        Summary.Add($"maps/: {maps} maps from {rendered + unchanged} map files, {rendered} rendered");
    }

    private void KeepMeta(uint id)
    {
        var path = Path.Combine(output.Root, "maps", id.ToString(), "meta.json");
        if (File.Exists(path))
            output.Write($"maps/{id}/meta.json", System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)));
    }

    private MapMeta? Render(string mapFile, uint puzzleSize, string key, string folder, out bool fresh)
    {
        fresh = false;
        var data = MapFiles.ReadDMap(files, mapFile, out var dmapStamp);
        if (data == null)
            return null;
        var drawing = MapFiles.ParseDMap(data, Scene);
        var puzzle = Puzzle.Parse(files.Read(drawing.Puzzle) ?? throw new InvalidDataException($"No {drawing.Puzzle}"));
        var grid = (int)puzzleSize;
        int width = puzzle.Width * grid, height = puzzle.Height * grid;
        int bgX = 32 * drawing.Width - width / 2;
        int bgY = 16 * drawing.Height - height / 2 + (drawing.Height % 2 == 1 ? 16 : 0);
        var maxZoom = 0;
        while (TileSize << maxZoom < Math.Max(width, height))
            maxZoom++;

        var meta = new MapMeta(width, height, TileSize, maxZoom,
            new Size(64, 32), new Size(drawing.Width, drawing.Height),
            new Point(32 * drawing.Width, 16), new Point(bgX, bgY),
            new PuzzleMeta(puzzle.Width, puzzle.Height, grid),
            $"maps/tiles/{key}/ground/{{z}}/{{x}}/{{y}}.webp", $"maps/tiles/{key}/covers/{{z}}/{{x}}/{{y}}.webp");

        var stamp = string.Join("|", Version, puzzleSize, dmapStamp, files.Stamp(drawing.Puzzle), files.Stamp(puzzle.Ani));
        var stampFile = Path.Combine(folder, ".stamp");
        var animationsFile = Path.Combine(folder, "animations.json");
        if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp)
            return File.Exists(animationsFile)
                ? meta with { Animations = JsonSerializer.Deserialize<List<Animation>>(File.ReadAllText(animationsFile), AnimationJson) }
                : meta;

        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        var puzzleFrames = Ani(puzzle.Ani);
        var animations = new List<Animation>();
        var ground = Place(drawing.Ground, drawing, bgX, bgY, key, folder, animations);
        var covers = Place(drawing.Covers, drawing, bgX, bgY, key, folder, animations);

        Pyramid(Path.Combine(folder, "ground"), maxZoom, width, height, (x, y) =>
        {
            var tile = new Rgba(TileSize, TileSize, new byte[TileSize * TileSize * 4]);
            int left = x * TileSize, top = y * TileSize;
            for (var ty = top / grid; ty <= (top + TileSize - 1) / grid && ty < puzzle.Height; ty++)
            for (var tx = left / grid; tx <= (left + TileSize - 1) / grid && tx < puzzle.Width; tx++)
            {
                var n = puzzle.Tiles[ty * puzzle.Width + tx];
                if (n != Puzzle.None && puzzleFrames?.GetValueOrDefault($"Puzzle{n}") is [var frame, ..] && Picture(frame) is { } picture)
                    Draw(tile, picture, tx * grid - left, ty * grid - top);
            }
            ground.Draw(tile, left, top);
            return tile;
        });
        Pyramid(Path.Combine(folder, "covers"), maxZoom, width, height, (x, y) =>
        {
            var tile = new Rgba(TileSize, TileSize, new byte[TileSize * TileSize * 4]);
            covers.Draw(tile, x * TileSize, y * TileSize);
            return tile;
        });
        if (animations.Count > 0)
            File.WriteAllText(animationsFile, JsonSerializer.Serialize(animations, AnimationJson));
        File.WriteAllText(stampFile, stamp);
        fresh = true;
        return animations.Count > 0 ? meta with { Animations = animations } : meta;
    }

    // The sprites in the order the client draws them, back to front, bucketed by the
    // max-zoom tiles they touch. Animated ones are written to anim/ and added to `animations`
    // instead.
    private Layer Place(List<MapSprite> list, MapDrawing drawing, int bgX, int bgY, string key, string folder, List<Animation> animations)
    {
        var placed = new List<(Rgba Picture, int X, int Y)>();
        foreach (var sprite in list.OrderBy(s => s.CellX + s.CellY).ThenBy(s => s.CellX))
        {
            var frames = Ani(sprite.Ani)?.GetValueOrDefault(sprite.Title);
            // A .msk is the mask that hides roles under a bridge the puzzle already shows
            if (frames is [var mask, ..] && mask.EndsWith(".msk", StringComparison.OrdinalIgnoreCase))
                continue;
            if (frames is not [var frame, ..] || Picture(frame) is not { } picture)
            {
                Errors.Add($"{sprite.Ani} [{sprite.Title}]: no picture");
                continue;
            }
            int x = 32 * (sprite.CellX - sprite.CellY) + 32 * drawing.Width + sprite.OffsetX - bgX;
            int y = 16 * (sprite.CellX + sprite.CellY) + 16 + sprite.OffsetY - bgY;
            if (frames.Count > 1 && sprite.Interval > 0 && Animate(frames, x, y, sprite.Interval, key, folder, animations))
                continue;
            placed.Add((picture, x, y));
        }
        return new Layer(placed);
    }

    // Writes the frames, each drawn at the same top left, padded to the largest of them
    private bool Animate(List<string> frames, int x, int y, int interval, string key, string folder, List<Animation> animations)
    {
        var pictures = frames.Select(Picture).ToList();
        if (pictures.Any(p => p == null))
            return false;
        int width = pictures.Max(p => p!.Width), height = pictures.Max(p => p!.Height);
        var n = animations.Count;
        var paths = new List<string>();
        for (var f = 0; f < pictures.Count; f++)
        {
            var padded = new Rgba(width, height, new byte[width * height * 4]);
            Draw(padded, pictures[f]!, 0, 0);
            var path = Path.Combine(folder, "anim", n.ToString(), $"{f}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Images.Png(padded));
            paths.Add($"maps/tiles/{key}/anim/{n}/{f}.png");
        }
        animations.Add(new Animation(x, y, width, height, interval, paths));
        return true;
    }

    private static readonly JsonSerializerOptions AnimationJson = new(JsonSerializerDefaults.Web);

    private sealed class Layer(List<(Rgba Picture, int X, int Y)> placed)
    {
        private readonly Dictionary<(int, int), List<int>> buckets = Bucket(placed);

        public void Draw(Rgba tile, int left, int top)
        {
            if (buckets.TryGetValue((left / TileSize, top / TileSize), out var indices))
                foreach (var i in indices)
                    MapTiles.Draw(tile, placed[i].Picture, placed[i].X - left, placed[i].Y - top);
        }

        private static Dictionary<(int, int), List<int>> Bucket(List<(Rgba Picture, int X, int Y)> placed)
        {
            var buckets = new Dictionary<(int, int), List<int>>();
            for (var i = 0; i < placed.Count; i++)
            {
                var (picture, x, y) = placed[i];
                for (var ty = Math.Max(0, y) / TileSize; ty <= (y + picture.Height - 1) / TileSize; ty++)
                for (var tx = Math.Max(0, x) / TileSize; tx <= (x + picture.Width - 1) / TileSize; tx++)
                {
                    if (!buckets.TryGetValue((tx, ty), out var list))
                        buckets[(tx, ty)] = list = new List<int>();
                    list.Add(i);
                }
            }
            return buckets;
        }
    }

    // Renders the max zoom tiles with `leaf` and halves them down to zoom 0, writing every tile
    // that isn't fully transparent
    private void Pyramid(string folder, int maxZoom, int width, int height, Func<int, int, Rgba> leaf)
    {
        Rgba? Tile(int z, int x, int y)
        {
            var scale = maxZoom - z;
            if ((long)x * TileSize << scale >= width || (long)y * TileSize << scale >= height)
                return null;
            Rgba? tile;
            if (z == maxZoom)
                tile = leaf(x, y);
            else
            {
                var children = new Rgba?[4];
                if (maxZoom - z > 3)
                    Parallel.For(0, 4, i => children[i] = Tile(z + 1, x * 2 + i % 2, y * 2 + i / 2));
                else
                    for (var i = 0; i < 4; i++)
                        children[i] = Tile(z + 1, x * 2 + i % 2, y * 2 + i / 2);
                tile = children.Any(c => c != null) ? Half(children) : null;
            }
            if (tile == null || !Visible(tile))
                return null;
            var path = Path.Combine(folder, z.ToString(), x.ToString(), $"{y}.webp");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Webp(tile));
            return tile;
        }
        Tile(0, 0, 0);
    }

    // Four tiles (top left, top right, bottom left, bottom right) averaged into one, with
    // the colour weighted by alpha so transparent edges don't darken
    private static Rgba Half(Rgba?[] children)
    {
        var half = new Rgba(TileSize, TileSize, new byte[TileSize * TileSize * 4]);
        const int h = TileSize / 2;
        for (var i = 0; i < 4; i++)
        {
            if (children[i] is not { } child)
                continue;
            int ox = i % 2 * h, oy = i / 2 * h;
            var src = child.Pixels;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < h; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (var k = 0; k < 4; k++)
                {
                    var s = ((y * 2 + k / 2) * TileSize + x * 2 + k % 2) * 4;
                    int alpha = src[s + 3];
                    r += src[s] * alpha;
                    g += src[s + 1] * alpha;
                    b += src[s + 2] * alpha;
                    a += alpha;
                }
                var d = ((oy + y) * TileSize + ox + x) * 4;
                if (a == 0)
                    continue;
                half.Pixels[d] = (byte)(r / a);
                half.Pixels[d + 1] = (byte)(g / a);
                half.Pixels[d + 2] = (byte)(b / a);
                half.Pixels[d + 3] = (byte)((a + 2) / 4);
            }
        }
        return half;
    }

    // Draws `picture` over `tile` with its top left at (x, y), alpha blended
    private static void Draw(Rgba tile, Rgba picture, int x, int y)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(tile.Width, x + picture.Width), y1 = Math.Min(tile.Height, y + picture.Height);
        for (var ty = y0; ty < y1; ty++)
        for (var tx = x0; tx < x1; tx++)
        {
            var s = ((ty - y) * picture.Width + tx - x) * 4;
            var d = (ty * tile.Width + tx) * 4;
            int a = picture.Pixels[s + 3];
            if (a == 0)
                continue;
            if (a == 255)
            {
                picture.Pixels.AsSpan(s, 4).CopyTo(tile.Pixels.AsSpan(d, 4));
                continue;
            }
            int da = tile.Pixels[d + 3] * (255 - a) / 255, oa = a + da;
            for (var c = 0; c < 3; c++)
                tile.Pixels[d + c] = (byte)((picture.Pixels[s + c] * a + tile.Pixels[d + c] * da) / oa);
            tile.Pixels[d + 3] = (byte)oa;
        }
    }

    private static bool Visible(Rgba tile)
    {
        for (var i = 3; i < tile.Pixels.Length; i += 4)
            if (tile.Pixels[i] != 0)
                return true;
        return false;
    }

    private static byte[] Webp(Rgba tile)
    {
        var info = new SKImageInfo(tile.Width, tile.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(tile.Pixels, GCHandleType.Pinned);
        try
        {
            using var image = SKImage.FromPixels(info, handle.AddrOfPinnedObject(), tile.Width * 4);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 82);
            return encoded.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    private Dictionary<string, List<string>>? Ani(string path) =>
        anis.GetOrAdd(path, p => files.Read(p) is { } data ? global::Ani.Parse(data) : null);

    private List<SceneSprite>? Scene(string path) =>
        scenes.GetOrAdd(path, p => files.Read(p) is { } data ? MapFiles.ParseScene(data) : null);

    // Scene and cover pictures repeat across a map, so they're decoded once. Puzzle tiles
    // mostly don't, and there are too many to keep.
    private Rgba? Picture(string path)
    {
        if (path.Contains("/puzzle/", StringComparison.OrdinalIgnoreCase))
            return Decode(path);
        return sprites.GetOrAdd(path, Decode);
    }

    private Rgba? Decode(string path)
    {
        try
        {
            return files.Read(path) is { } data ? Images.Decode(data) : null;
        }
        catch (Exception e) when (e is not IOException)
        {
            Errors.Add($"{path}: {e.Message}");
            return null;
        }
    }

    // An animated sprite: its top left in background pixels, its size, the time each frame
    // shows in ms and the frames' paths
    private sealed record Animation(int X, int Y, int Width, int Height, int Interval, List<string> Frames);
    private sealed record MapMeta(int Width, int Height, int TileSize, int MaxZoom, Size Cell, Size MapSize,
        Point Origin, Point Background, PuzzleMeta Puzzle, string Ground, string Covers)
    {
        public List<Animation>? Animations { get; init; }
    }
    private sealed record Size(int Width, int Height);
    private sealed record Point(int X, int Y);
    private sealed record PuzzleMeta(int Width, int Height, int GridSize);
}

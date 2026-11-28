using System.Collections.Concurrent;
using System.Text.Json;
using Fetih.Game.World;
using Fetih.Web.Features.Maps;

// Prerenders low-zoom Leaflet tiles (z = 0..2) and metadata/animations for each map; higher
// zoom levels are rendered on demand by Fetih.Web's MapTileService.
sealed class MapTiles(ClientFiles files, Output output)
{
    // Raise it when the rendering changes, so every map is rendered again
    public const string Version = "3";
    public const int TileSize = MapTileRenderer.TileSize;
    public const int PrerenderMaxZoom = 2;

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
        var animations = new List<Animation>();
        CollectAnimations(drawing.Ground, drawing, bgX, bgY, key, folder, animations);
        CollectAnimations(drawing.Covers, drawing, bgX, bgY, key, folder, animations);

        var renderer = new MapTileRenderer(files.Read);
        var rendered = renderer.LoadMap(mapFile, puzzleSize);
        if (rendered != null)
        {
            int topZoom = Math.Min(PrerenderMaxZoom, rendered.MaxZoom);
            foreach (var layer in new[] { "ground", "covers" })
            {
                for (int z = 0; z <= topZoom; z++)
                {
                    int scale = 1 << (rendered.MaxZoom - z);
                    int tilesX = (int)Math.Ceiling((double)rendered.Width / (TileSize * scale));
                    int tilesY = (int)Math.Ceiling((double)rendered.Height / (TileSize * scale));
                    for (int x = 0; x < tilesX; x++)
                    for (int y = 0; y < tilesY; y++)
                    {
                        var bytes = rendered.RenderTile(layer, z, x, y);
                        if (bytes == null)
                            continue;
                        var path = Path.Combine(folder, layer, z.ToString(), x.ToString(), $"{y}.webp");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
            }
        }
        if (animations.Count > 0)
            File.WriteAllText(animationsFile, JsonSerializer.Serialize(animations, AnimationJson));
        File.WriteAllText(stampFile, stamp);
        fresh = true;
        return animations.Count > 0 ? meta with { Animations = animations } : meta;
    }

    private void CollectAnimations(List<MapSprite> list, MapDrawing drawing, int bgX, int bgY, string key, string folder, List<Animation> animations)
    {
        foreach (var sprite in list.OrderBy(s => s.CellX + s.CellY).ThenBy(s => s.CellX))
        {
            var frames = Ani(sprite.Ani)?.GetValueOrDefault(sprite.Title);
            if (frames is [var mask, ..] && mask.EndsWith(".msk", StringComparison.OrdinalIgnoreCase))
                continue;
            if (frames is null or { Count: <= 1 } || sprite.Interval <= 0)
                continue;
            int x = 32 * (sprite.CellX - sprite.CellY) + 32 * drawing.Width + sprite.OffsetX - bgX;
            int y = 16 * (sprite.CellX + sprite.CellY) + 16 + sprite.OffsetY - bgY;
            Animate(frames, x, y, sprite.Interval, key, folder, animations);
        }
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

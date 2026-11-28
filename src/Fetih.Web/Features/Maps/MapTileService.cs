namespace Fetih.Web.Features.Maps;

using System.Collections.Concurrent;
using Fetih.Game.World;

// Renders higher-zoom map tiles on demand from client files and caches them on disk up to TileCacheMaxBytes (LRU).
public sealed class MapTileService : IDisposable
{
    private const int MaxLoadedMaps = 4;

    private readonly string? clientDir;
    private readonly string? cacheDir;
    private readonly long maxCacheBytes;
    private readonly Lazy<TileWdfArchive?> archive;
    private readonly Lazy<Dictionary<string, (string MapFile, uint PuzzleSize)>> mapsByKey;
    private readonly object lruLock = new();
    private readonly Dictionary<string, (RenderedTileMap Map, LinkedListNode<string> Node)> loadedMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> mapOrder = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> inFlight = new();
    private int evicting;

    public MapTileService(WebConfiguration config)
    {
        this.clientDir = WebConfiguration.PathsSection.Resolve(config.Paths.ClientDirectory);
        this.cacheDir = WebConfiguration.PathsSection.Resolve(config.Paths.TileCacheDirectory);
        this.maxCacheBytes = Math.Max(0, config.Paths.TileCacheMaxBytes);
        this.archive = new Lazy<TileWdfArchive?>(this.OpenArchive, LazyThreadSafetyMode.ExecutionAndPublication);
        this.mapsByKey = new Lazy<Dictionary<string, (string, uint)>>(this.LoadMapIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<byte[]?> GetTileAsync(string key, string layer, int z, int x, int y)
    {
        if (string.IsNullOrEmpty(key) || key.IndexOfAny(['/', '\\', '.']) >= 0)
            return null;
        if (layer is not ("ground" or "covers") || z < 0 || x < 0 || y < 0)
            return null;

        if (!this.mapsByKey.Value.TryGetValue(key, out var entry))
            return null;

        if (this.cacheDir != null)
        {
            string webpPath = Path.Combine(this.cacheDir, key, layer, z.ToString(), x.ToString(), $"{y}.webp");
            string emptyPath = Path.Combine(this.cacheDir, key, layer, z.ToString(), x.ToString(), $"{y}.empty");
            if (File.Exists(webpPath))
            {
                Touch(webpPath);
                return await File.ReadAllBytesAsync(webpPath);
            }
            if (File.Exists(emptyPath))
            {
                Touch(emptyPath);
                return null;
            }
        }

        string tileKey = $"{key}/{layer}/{z}/{x}/{y}";
        var task = this.inFlight.GetOrAdd(tileKey, _ => new Lazy<Task<byte[]?>>(() =>
            Task.Run(() => this.RenderAndCache(key, entry.MapFile, entry.PuzzleSize, layer, z, x, y))));
        try
        {
            return await task.Value;
        }
        finally
        {
            this.inFlight.TryRemove(tileKey, out _);
        }
    }

    private byte[]? RenderAndCache(string key, string mapFile, uint puzzleSize, string layer, int z, int x, int y)
    {
        var map = this.GetOrLoadMap(key, mapFile, puzzleSize);
        if (map == null || !map.IsValidCoord(z, x, y))
            return null;

        byte[]? bytes = map.RenderTile(layer, z, x, y);
        if (this.cacheDir != null)
        {
            string dir = Path.Combine(this.cacheDir, key, layer, z.ToString(), x.ToString());
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, bytes != null ? $"{y}.webp" : $"{y}.empty");
            string tmp = Path.Combine(dir, $".tmp.{y}.{Guid.NewGuid():N}");
            try
            {
                File.WriteAllBytes(tmp, bytes ?? []);
                File.Move(tmp, target, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }

            if (this.maxCacheBytes > 0 && Interlocked.CompareExchange(ref this.evicting, 1, 0) == 0)
            {
                _ = Task.Run(this.EvictIfNeeded);
            }
        }
        return bytes;
    }

    private RenderedTileMap? GetOrLoadMap(string key, string mapFile, uint puzzleSize)
    {
        var arc = this.archive.Value;
        if (arc == null)
            return null;

        lock (this.lruLock)
        {
            if (this.loadedMaps.TryGetValue(key, out var existing))
            {
                this.mapOrder.Remove(existing.Node);
                this.mapOrder.AddFirst(existing.Node);
                return existing.Map;
            }
        }

        var renderer = new MapTileRenderer(arc.Read);
        var loaded = renderer.LoadMap(mapFile, puzzleSize);
        if (loaded == null)
            return null;

        lock (this.lruLock)
        {
            if (this.loadedMaps.TryGetValue(key, out var existing))
            {
                this.mapOrder.Remove(existing.Node);
                this.mapOrder.AddFirst(existing.Node);
                return existing.Map;
            }

            var node = this.mapOrder.AddFirst(key);
            this.loadedMaps[key] = (loaded, node);
            while (this.loadedMaps.Count > MaxLoadedMaps && this.mapOrder.Last is { } oldest)
            {
                this.mapOrder.RemoveLast();
                this.loadedMaps.Remove(oldest.Value);
            }
            return loaded;
        }
    }

    private void EvictIfNeeded()
    {
        try
        {
            if (this.cacheDir == null || !Directory.Exists(this.cacheDir))
                return;

            var files = new DirectoryInfo(this.cacheDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Name.StartsWith(".tmp.", StringComparison.Ordinal))
                .Select(f => (Info: f, f.Length, f.LastWriteTimeUtc))
                .ToList();

            long total = files.Sum(f => f.Length);
            if (total <= this.maxCacheBytes)
                return;

            long target = (long)(this.maxCacheBytes * 0.85);
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= target)
                    break;
                try
                {
                    f.Info.Delete();
                    total -= f.Length;
                }
                catch { }
            }
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref this.evicting, 0);
        }
    }

    private static void Touch(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - File.GetLastWriteTimeUtc(path)).TotalSeconds > 60)
                File.SetLastWriteTimeUtc(path, now);
        }
        catch { }
    }

    private TileWdfArchive? OpenArchive()
    {
        if (string.IsNullOrEmpty(this.clientDir) || !Directory.Exists(this.clientDir))
            return null;
        try { return new TileWdfArchive(this.clientDir); }
        catch { return null; }
    }

    private Dictionary<string, (string MapFile, uint PuzzleSize)> LoadMapIndex()
    {
        var result = new Dictionary<string, (string, uint)>(StringComparer.OrdinalIgnoreCase);
        var arc = this.archive.Value;
        var dat = arc?.Read("ini/GameMap.dat");
        if (dat == null)
            return result;

        foreach (var entry in GameMapIndex.Parse(dat).Values)
        {
            string key = Path.GetFileNameWithoutExtension(entry.Path).ToLowerInvariant();
            result.TryAdd(key, (entry.Path, entry.PuzzleSize));
        }
        return result;
    }

    public void Dispose()
    {
        if (this.archive.IsValueCreated)
            this.archive.Value?.Dispose();
    }
}

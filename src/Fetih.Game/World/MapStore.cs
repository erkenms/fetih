namespace Fetih.Game.World
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using Fetih.Core.Logging;
    using SharpCompress.Archives.SevenZip;

    public sealed class MapStore
    {
        private readonly string ClientDirectory;
        private readonly Dictionary<uint, GameMapEntry> Entries;
        private readonly ConcurrentDictionary<uint, Lazy<MapGrid>> Grids =
            new ConcurrentDictionary<uint, Lazy<MapGrid>>();
        private readonly ConcurrentDictionary<string, IReadOnlyList<ScenePart>> Scenes =
            new ConcurrentDictionary<string, IReadOnlyList<ScenePart>>(StringComparer.OrdinalIgnoreCase);

        public int Count => this.Entries.Count;

        public IEnumerable<uint> MapIDs => this.Entries.Keys;

        public MapStore(string clientDirectory)
        {
            this.ClientDirectory = Path.GetFullPath(clientDirectory);
            var index = ResolveFile(this.ClientDirectory, "ini/GameMap.dat")
                ?? throw new FileNotFoundException(
                    $"No ini/GameMap.dat in {this.ClientDirectory}");
            this.Entries = GameMapIndex.Parse(File.ReadAllBytes(index), out var duplicates);
            foreach (var duplicate in duplicates)
                Serilog.Log.ForContext<MapStore>().Warning(
                    "GameMap.dat lists map {MapId} again with {Path}; using {FirstPath}",
                    duplicate.Id, duplicate.Path, this.Entries[duplicate.Id].Path);
            LogEvents.For<MapStore>(LogEvents.MapIndexLoaded).Information(
                "Found {MapCount} maps in {Directory}", this.Entries.Count, this.ClientDirectory);
        }

        public bool Exists(uint mapID) => this.Entries.ContainsKey(mapID);

        public MapGrid Get(uint mapID)
        {
            if (!this.Entries.TryGetValue(mapID, out var entry))
                throw new KeyNotFoundException($"Map {mapID} isn't in GameMap.dat");
            return this.Grids.GetOrAdd(mapID, _ => new Lazy<MapGrid>(
                () => this.Load(entry), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        private MapGrid Load(GameMapEntry entry)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var grid = MapGrid.Parse(this.ReadDMap(entry), this.ReadScene);
                LogEvents.For<MapStore>(LogEvents.MapLoaded).Debug(
                    "Loaded map {MapId} ({Width}x{Height}, {PortalCount} portals) in {LoadMs} ms",
                    entry.Id, grid.Width, grid.Height, grid.Portals.Count,
                    watch.ElapsedMilliseconds);
                return grid;
            }
            catch (Exception e)
            {
                LogEvents.For<MapStore>(LogEvents.MapLoadFailed).Error(e,
                    "Couldn't load map {MapId} from {Path}", entry.Id, entry.Path);
                throw;
            }
        }

        private IReadOnlyList<ScenePart> ReadScene(string path) =>
            this.Scenes.GetOrAdd(path.Replace('\\', '/'), p =>
            {
                var file = ResolveFile(this.ClientDirectory, p)
                    ?? throw new InvalidDataException($"Scene file {p} not found");
                return SceneFile.Parse(File.ReadAllBytes(file));
            });

        private byte[] ReadDMap(GameMapEntry entry)
        {
            if (entry.Path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                var archivePath = ResolveFile(this.ClientDirectory, entry.Path);
                if (archivePath != null)
                {
                    using var archive = SevenZipArchive.OpenArchive(archivePath);
                    var dmap = archive.Entries.FirstOrDefault(e => !e.IsDirectory &&
                        e.Key.EndsWith(".dmap", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"{entry.Path} holds no DMap file");
                    using var stream = dmap.OpenEntryStream();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }

            var plainPath = ResolveFile(this.ClientDirectory, Path.ChangeExtension(entry.Path, ".DMap"))
                ?? throw new InvalidDataException($"Map file {entry.Path} not found");
            return File.ReadAllBytes(plainPath);
        }

        // The client's lists don't always match file name case, which matters on Linux.
        public static string ResolveFile(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(relativePath))
                return null;
            string current = root;
            var parts = relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                string exact = Path.Combine(current, parts[i]);
                if (last ? File.Exists(exact) : Directory.Exists(exact))
                {
                    current = exact;
                    continue;
                }

                if (!Directory.Exists(current))
                    return null;
                var match = (last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current))
                    .FirstOrDefault(p => string.Equals(
                        Path.GetFileName(p), parts[i], StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return null;
                current = match;
            }

            return current;
        }
    }
}

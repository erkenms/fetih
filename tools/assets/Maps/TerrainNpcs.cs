using System.Collections.Concurrent;

// NPCs the client draws as scenery instead of a 3D role: roles 25, 26, 39, 41, 42, 102 and 103
// (city statues, guild war gates, family furniture). ini/terrainnpc.ini gives, per look / 10,
// one scene file per direction (look % 10), drawn like a DMap terrain object at the NPC's cell.
// A part whose frames are .msk files is drawn from the .dds beside each of them.
sealed class TerrainNpcs(ClientFiles files, Output output)
{
    public List<string> Summary { get; } = new();

    public void Build()
    {
        var ini = files.Read("ini/terrainnpc.ini");
        if (ini == null)
        {
            Summary.Add("terrainNpcs.json: no ini/terrainnpc.ini, left out");
            return;
        }

        var looks = new SortedDictionary<int, string>();
        int? type = null;
        foreach (var raw in ClientFiles.Gbk.GetString(ini).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("[NpcType", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
                type = int.TryParse(line[8..^1], out var t) ? t : null;
            else if (type != null && line.StartsWith("Dir", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is > 3 and var eq
                     && int.TryParse(line[3..eq], out var dir) && dir is >= 0 and < 10)
                looks[type.Value * 10 + dir] = line[(eq + 1)..].Trim().Replace('\\', '/');
        }

        var scenes = new ConcurrentDictionary<string, List<TerrainPart>>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(looks.Values.Distinct(StringComparer.OrdinalIgnoreCase), scene =>
        {
            if (Scene(scene) is { Count: > 0 } parts)
                scenes[Key(scene)] = parts;
        });

        var byLook = looks.Where(l => scenes.ContainsKey(Key(l.Value)))
            .ToDictionary(l => l.Key.ToString(), l => Key(l.Value));
        output.Write("terrainNpcs.json", new
        {
            looks = byLook,
            scenes = new SortedDictionary<string, List<TerrainPart>>(scenes, StringComparer.Ordinal),
        }, omitZero: true);
        Summary.Add($"terrainNpcs.json: {byLook.Count} looks drawing {scenes.Count} scenes " +
            $"({scenes.Values.Count(s => s.Any(p => p.Frames.Count > 1))} animated), " +
            $"{looks.Count - byLook.Count} looks without a picture");
    }

    // A part's top left is (X, Y) from the world pixel of the NPC's cell
    private List<TerrainPart>? Scene(string path)
    {
        var data = files.Read(path);
        if (data == null)
            return null;
        var parts = new List<TerrainPart>();
        foreach (var part in MapFiles.ParseScene(data))
        {
            var ani = files.Read(part.Ani) is { } a ? Ani.Parse(a) : null;
            if (ani?.GetValueOrDefault(part.Title) is not { Count: > 0 } frames)
                continue;
            var sources = frames.Select(f => f.EndsWith(".msk", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(f, ".dds") : f).ToList();
            var pictures = new List<(string Source, Rgba Picture)>();
            foreach (var source in sources)
            {
                try
                {
                    if (files.Read(source) is { } bytes)
                        pictures.Add((source, Images.Decode(bytes)));
                }
                catch (Exception e) when (e is not IOException)
                {
                    output.Errors.Add($"{source}: {e.Message}");
                }
            }
            if (pictures.Count != sources.Count)
                continue;

            int width = pictures.Max(p => p.Picture.Width), height = pictures.Max(p => p.Picture.Height);
            var paths = new List<string>();
            foreach (var (source, picture) in pictures)
            {
                var name = source.ToLowerInvariant().Replace("data/map/", "");
                var padded = picture.Width != width || picture.Height != height;
                var target = $"terrain/{Path.ChangeExtension(name, null)}{(padded ? $"-{width}x{height}" : "")}.png";
                if (output.Image(target, $"{source}@{files.Stamp(source)}#{width}x{height}", () => Pad(picture, width, height)))
                    paths.Add(target);
            }
            if (paths.Count == pictures.Count)
                parts.Add(new TerrainPart(32 * (part.CellX - part.CellY) + part.PixelX, 16 * (part.CellX + part.CellY) + part.PixelY,
                    width, height, paths.Count > 1 ? part.Interval : 0, paths));
        }
        return parts;
    }

    private static Rgba Pad(Rgba picture, int width, int height)
    {
        if (picture.Width == width && picture.Height == height)
            return picture;
        var padded = new Rgba(width, height, new byte[width * height * 4]);
        for (var y = 0; y < picture.Height; y++)
            picture.Pixels.AsSpan(y * picture.Width * 4, picture.Width * 4).CopyTo(padded.Pixels.AsSpan(y * width * 4));
        return padded;
    }

    private static string Key(string scene) =>
        Path.ChangeExtension(scene.ToLowerInvariant().Replace("map/scene/", ""), null);

    private sealed record TerrainPart(int X, int Y, int Width, int Height, int Interval, List<string> Frames);
}

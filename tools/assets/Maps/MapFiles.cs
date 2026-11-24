using System.Buffers.Binary;
using System.Text;
using SharpCompress.Archives.SevenZip;

// A picture of an .ani section drawn on the map. Its top left corner is the world pixel of
// cell (CellX, CellY) plus (OffsetX, OffsetY).
sealed record MapSprite(string Ani, string Title, int CellX, int CellY, int OffsetX, int OffsetY);

// What the client draws of a map: the puzzle background, the terrain objects' scene parts
// (bridges, platforms) on the ground, and the covers (buildings, trees, roofs) that it sorts
// with the roles and so draws over them.
sealed record MapDrawing(string Puzzle, int Width, int Height, List<MapSprite> Ground, List<MapSprite> Covers);

// map/puzzle/*.pul: char[8] "PUZZLE" or "PUZZLE2", char[256] .ani file, uint32 width, height
// in tiles, then width * height uint16 tile numbers row by row; tile n is the .ani section
// "Puzzle<n>". PUZZLE2 adds 8 bytes the background doesn't need.
sealed record Puzzle(string Ani, int Width, int Height, ushort[] Tiles)
{
    public const ushort None = 0xFFFF;

    public static Puzzle Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 272 || !data[..6].SequenceEqual("PUZZLE"u8))
            throw new InvalidDataException("Not a puzzle file");
        var ani = MapFiles.Text(data.Slice(8, 256));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[264..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[268..]);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || data.Length < 272 + width * height * 2)
            throw new InvalidDataException($"Puzzle has a bad size {width}x{height}");
        var tiles = new ushort[width * height];
        for (var i = 0; i < tiles.Length; i++)
            tiles[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(272 + i * 2)..]);
        return new Puzzle(ani, width, height, tiles);
    }
}

static class MapFiles
{
    // The DMap of a GameMap.dat entry: inside its .7z, or next to it as .DMap
    public static byte[]? ReadDMap(ClientFiles files, string path, out string? stamp)
    {
        if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && files.Read(path) is { } packed)
        {
            stamp = files.Stamp(path);
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
        var plain = Path.ChangeExtension(path, ".DMap");
        stamp = files.Stamp(plain);
        return files.Read(plain);
    }

    // The DMap layout is in Comet.Game's MapGrid. Terrain objects (type 1) name a scene file,
    // whose parts sit at the object's cell plus the part's cell offset and are drawn at that
    // cell's pixel plus the part's pixel offset. Covers (type 4) are char[260] .ani file,
    // char[128] section, int32 cell x, y, base width, height, pixel offset x, y, frame
    // interval; they're drawn at the cell's pixel minus the offset.
    public static MapDrawing ParseDMap(ReadOnlySpan<byte> data, Func<string, List<SceneSprite>?> scene)
    {
        var puzzle = Text(data.Slice(8, 260));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[268..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[272..]);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidDataException($"DMap has a bad size {width}x{height}");
        var offset = 276 + height * (width * 6 + 4);
        var portals = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4 + portals * 12;
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4;

        var ground = new List<MapSprite>();
        var covers = new List<MapSprite>();
        for (var i = 0; i < count; i++)
        {
            var type = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            var o = data[(offset + 4)..];
            switch (type)
            {
                case 1:
                    int x = BinaryPrimitives.ReadInt32LittleEndian(o[260..]), y = BinaryPrimitives.ReadInt32LittleEndian(o[264..]);
                    foreach (var part in scene(Text(o[..260])) ?? [])
                        ground.Add(new MapSprite(part.Ani, part.Title, x + part.CellX, y + part.CellY, part.PixelX, part.PixelY));
                    break;
                case 4:
                    covers.Add(new MapSprite(Text(o[..260]), Text(o.Slice(260, 128)),
                        BinaryPrimitives.ReadInt32LittleEndian(o[388..]), BinaryPrimitives.ReadInt32LittleEndian(o[392..]),
                        -BinaryPrimitives.ReadInt32LittleEndian(o[404..]), -BinaryPrimitives.ReadInt32LittleEndian(o[408..])));
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
        return new MapDrawing(puzzle, width, height, ground, covers);
    }

    // map/Scene/*.scene parts, as Comet.Game's SceneFile reads them, with the picture: char[256]
    // .ani file, char[64] section, int32 pixel offset x, y, then after interval, width,
    // height and thickness the cell offset x, y.
    public static List<SceneSprite> ParseScene(ReadOnlySpan<byte> data)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        var parts = new List<SceneSprite>();
        var offset = 4;
        for (var p = 0; p < count; p++)
        {
            var h = data[offset..];
            int width = BinaryPrimitives.ReadInt32LittleEndian(h[332..]), height = BinaryPrimitives.ReadInt32LittleEndian(h[336..]);
            parts.Add(new SceneSprite(Text(h[..256]), Text(h.Slice(256, 64)),
                BinaryPrimitives.ReadInt32LittleEndian(h[320..]), BinaryPrimitives.ReadInt32LittleEndian(h[324..]),
                BinaryPrimitives.ReadInt32LittleEndian(h[344..]), BinaryPrimitives.ReadInt32LittleEndian(h[348..])));
            offset += 356 + width * height * 12;
        }
        return parts;
    }

    public static string Text(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]).Replace('\\', '/').Trim();
    }
}

sealed record SceneSprite(string Ani, string Title, int PixelX, int PixelY, int CellX, int CellY);

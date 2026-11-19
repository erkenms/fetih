namespace Comet.Game.World
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>A portal cell in a map: stepping on it uses the map's portal number.</summary>
    /// <param name="X">Cell X</param>
    /// <param name="Y">Cell Y</param>
    /// <param name="Index">Portal number within the map; where it leads is server data</param>
    public readonly record struct MapPortal(ushort X, ushort Y, uint Index);

    /// <summary>
    /// The cell grid of a map, read from the client's DMap file and the scene files its
    /// terrain objects place: which cells can be walked on, each cell's elevation (for
    /// jump checks), and the portal cells.
    /// </summary>
    /// <remarks>
    /// <para>5619: from client data (checked against newbie and newplain). Layout, all
    /// little-endian:</para>
    /// <code>
    /// 0    uint32   version
    /// 4    uint32   data
    /// 8    char[260] puzzle file path
    /// 268  uint32   width
    /// 272  uint32   height
    /// 276  rows: width cells of (uint16 access, uint16 surface, int16 elevation),
    ///      then a uint32 row checksum
    ///      uint32 portal count, then per portal uint32 x, y, index
    ///      uint32 object count, then per object a uint32 type and:
    ///        1  terrain: char[260] scene file, int32 x, y      (272 bytes in all)
    ///        4  cover:   char[260] animation, char[128] title,
    ///                    7 int32                               (420)
    ///        10 effect:  char[64] name, int32 x, y             (76)
    ///        15 sound:   char[260] file, int32 x, y, range,
    ///                    volume                                (280)
    /// </code>
    /// <para>An access value of 0 means the cell can be walked on. The base grid blocks
    /// most raised ground: bridges, platforms and stairs are terrain objects, whose
    /// scene cells replace the base cells under them (see <see cref="SceneFile"/>).
    /// On map 1010 the base grid alone blocks 26 of the 85 cells seen in VM jumps.
    /// Every object in the 5619 maps is one of the four types above.</para>
    /// </remarks>
    public sealed class MapGrid
    {
        // Constants
        private const int HeaderSize = 276;
        private const int CellSize = 6;
        private const int MaxSide = 4096;

        private readonly bool[] Walkable;
        private readonly short[] Elevations;
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<MapPortal> Portals { get; }

        private MapGrid(int width, int height, bool[] walkable, short[] elevations,
            IReadOnlyList<MapPortal> portals)
        {
            this.Width = width;
            this.Height = height;
            this.Walkable = walkable;
            this.Elevations = elevations;
            this.Portals = portals;
        }

        /// <summary>True if the cell is inside the map.</summary>
        public bool Contains(int x, int y) =>
            x >= 0 && y >= 0 && x < this.Width && y < this.Height;

        /// <summary>True if the cell is inside the map and can be walked on.</summary>
        public bool IsWalkable(int x, int y) =>
            this.Contains(x, y) && this.Walkable[y * this.Width + x];

        /// <summary>Elevation of a cell inside the map.</summary>
        public short Elevation(int x, int y) =>
            this.Contains(x, y) ? this.Elevations[y * this.Width + x]
                : throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the map");

        /// <summary>Parses a DMap file without applying its terrain objects.</summary>
        /// <exception cref="InvalidDataException">The file is cut short or malformed.</exception>
        public static MapGrid Parse(ReadOnlySpan<byte> data) => Parse(data, null);

        /// <summary>Parses a DMap file and applies its terrain objects' scenes.</summary>
        /// <param name="data">DMap file</param>
        /// <param name="scenes">
        /// Returns the parts of a scene file given its path from the DMap (e.g.
        /// map\Scene\stand06.scene); null to ignore scene objects
        /// </param>
        /// <exception cref="InvalidDataException">The file is cut short or malformed.</exception>
        public static MapGrid Parse(ReadOnlySpan<byte> data, Func<string, IReadOnlyList<ScenePart>> scenes)
        {
            if (data.Length < HeaderSize)
                throw new InvalidDataException("DMap header is cut short");
            int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[268..]);
            int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[272..]);
            if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
                throw new InvalidDataException($"DMap has a bad size {width}x{height}");

            int rowSize = width * CellSize + 4;
            int cellsEnd = HeaderSize + height * rowSize;
            if (data.Length < cellsEnd + 4)
                throw new InvalidDataException("DMap cells are cut short");

            var walkable = new bool[width * height];
            var elevations = new short[width * height];
            for (int y = 0; y < height; y++)
            {
                var row = data.Slice(HeaderSize + y * rowSize, width * CellSize);
                for (int x = 0; x < width; x++)
                {
                    var cell = row.Slice(x * CellSize, CellSize);
                    walkable[y * width + x] = BinaryPrimitives.ReadUInt16LittleEndian(cell) == 0;
                    elevations[y * width + x] = BinaryPrimitives.ReadInt16LittleEndian(cell[4..]);
                }
            }

            int offset = cellsEnd;
            uint portalCount = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            if (portalCount > 1024 || data.Length < offset + portalCount * 12)
                throw new InvalidDataException("DMap portals are cut short");

            var portals = new MapPortal[portalCount];
            for (int i = 0; i < portalCount; i++, offset += 12)
                portals[i] = new MapPortal(
                    (ushort)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]),
                    (ushort)BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 8)..]));

            if (scenes != null)
                ApplyObjects(data, offset, width, height, walkable, elevations, scenes);
            return new MapGrid(width, height, walkable, elevations, portals);
        }

        /// <summary>
        /// Reads the objects after the portal list and lays each terrain object's scene
        /// cells over the grid.
        /// </summary>
        private static void ApplyObjects(ReadOnlySpan<byte> data, int offset, int width,
            int height, bool[] walkable, short[] elevations,
            Func<string, IReadOnlyList<ScenePart>> scenes)
        {
            if (data.Length < offset + 4)
                throw new InvalidDataException("DMap object list is cut short");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            for (uint i = 0; i < count; i++)
            {
                if (data.Length < offset + 4)
                    throw new InvalidDataException("DMap objects are cut short");
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
                int size = type switch { 1 => 272, 4 => 420, 10 => 76, 15 => 280, _ => 0 };
                if (size == 0)
                    throw new InvalidDataException($"DMap object {i} has an unknown type {type}");
                if (data.Length < offset + size)
                    throw new InvalidDataException("DMap objects are cut short");

                if (type == 1)
                {
                    var name = data.Slice(offset + 4, 260);
                    int end = name.IndexOf((byte)0);
                    string path = System.Text.Encoding.Latin1.GetString(end < 0 ? name : name[..end]);
                    int objectX = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 264)..]);
                    int objectY = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 268)..]);
                    foreach (var part in scenes(path))
                    for (int j = 0; j < part.Height; j++)
                    for (int k = 0; k < part.Width; k++)
                    {
                        int x = objectX + part.OffsetX - k;
                        int y = objectY + part.OffsetY - j;
                        if (x < 0 || y < 0 || x >= width || y >= height)
                            continue;
                        var cell = part[k, j];
                        walkable[y * width + x] = cell.Walkable;
                        elevations[y * width + x] = cell.Elevation;
                    }
                }

                offset += size;
            }
        }
    }
}

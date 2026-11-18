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
    /// The cell grid of a map, read from the client's DMap file: which cells can be
    /// walked on, each cell's elevation (for jump checks), and the portal cells.
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
    ///      scene objects (decoration, sound, effects; not read)
    /// </code>
    /// <para>An access value of 0 means the cell can be walked on. Buildings and walls
    /// are already blocked in this grid, so the scene objects aren't needed for it.</para>
    /// </remarks>
    public sealed class MapGrid
    {
        // Constants
        private const int HeaderSize = 276;
        private const int CellSize = 6;
        private const int MaxSide = 4096;

        // Fields and properties
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

        /// <summary>Parses a DMap file.</summary>
        /// <exception cref="InvalidDataException">The file is cut short or malformed.</exception>
        public static MapGrid Parse(ReadOnlySpan<byte> data)
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

            return new MapGrid(width, height, walkable, elevations, portals);
        }
    }
}

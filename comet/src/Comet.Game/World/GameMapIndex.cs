namespace Comet.Game.World
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>A map listed in the client's GameMap.dat.</summary>
    /// <param name="Id">Map ID used by the server and the client</param>
    /// <param name="Path">Map file path relative to the client folder, e.g. map/map/newplain.7z</param>
    /// <param name="PuzzleSize">Size of the map's puzzle (background) tiles</param>
    public sealed record GameMapEntry(uint Id, string Path, uint PuzzleSize);

    /// <summary>
    /// Reads the client's ini/GameMap.dat, which lists every map ID with the file that
    /// holds its cell grid.
    /// </summary>
    /// <remarks>
    /// 5619: from client data. A map count, then per map: ID, path length, path (no
    /// terminator), puzzle size; all numbers are little-endian uint32.
    /// </remarks>
    public static class GameMapIndex
    {
        /// <summary>Parses GameMap.dat.</summary>
        /// <exception cref="InvalidDataException">The file is cut short or malformed.</exception>
        public static Dictionary<uint, GameMapEntry> Parse(ReadOnlySpan<byte> data) =>
            Parse(data, out _);

        /// <summary>
        /// Parses GameMap.dat. If a map ID is listed more than once (5619 lists 1067
        /// twice), the first entry is used and the others are returned as duplicates.
        /// </summary>
        /// <exception cref="InvalidDataException">The file is cut short or malformed.</exception>
        public static Dictionary<uint, GameMapEntry> Parse(
            ReadOnlySpan<byte> data, out List<GameMapEntry> duplicates)
        {
            var maps = new Dictionary<uint, GameMapEntry>();
            duplicates = new List<GameMapEntry>();
            int offset = 0;
            uint count = ReadUInt32(data, ref offset);
            for (uint i = 0; i < count; i++)
            {
                uint id = ReadUInt32(data, ref offset);
                int length = (int)ReadUInt32(data, ref offset);
                if (length < 0 || offset + length > data.Length)
                    throw new InvalidDataException($"GameMap.dat: map {id} has a bad path length");
                string path = Encoding.Latin1.GetString(data.Slice(offset, length));
                offset += length;
                uint puzzleSize = ReadUInt32(data, ref offset);
                var entry = new GameMapEntry(id, path.Replace('\\', '/'), puzzleSize);
                if (!maps.TryAdd(id, entry))
                    duplicates.Add(entry);
            }

            return maps;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
        {
            if (offset + 4 > data.Length)
                throw new InvalidDataException("GameMap.dat ends early");
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            return value;
        }
    }
}

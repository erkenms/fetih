namespace Fetih.Game.World
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public sealed record GameMapEntry(uint Id, string Path, uint PuzzleSize);

    // ini/GameMap.dat, little-endian: uint32 count, then per map uint32 ID, uint32 path
    // length, path (no terminator), uint32 puzzle size.
    public static class GameMapIndex
    {
        public static Dictionary<uint, GameMapEntry> Parse(ReadOnlySpan<byte> data) =>
            Parse(data, out _);

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

namespace Fetih.Game.World
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;

    public readonly record struct SceneCell(bool Walkable, short Elevation);

    // Cell (i, j) lands at (objectX + OffsetX - i, objectY + OffsetY - j): scenes are laid out
    // backwards from the terrain object's position.
    public sealed record ScenePart(int Width, int Height, int OffsetX, int OffsetY, SceneCell[] Cells)
    {
        public SceneCell this[int i, int j] => this.Cells[j * this.Width + i];
    }

    // map/Scene/*.scene, little-endian:
    // uint32 part count, then per part:
    //   char[256] animation, char[64] title,
    //   int32 pixel offset x, y, animation interval,
    //   int32 width, height, thickness, cell offset x, y, height,
    //   width * height cells of int32 access, terrain, altitude (row by row)
    public static class SceneFile
    {
        private const int PartHeaderSize = 256 + 64 + 9 * 4;
        private const int CellSize = 12;

        public static List<ScenePart> Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
                throw new InvalidDataException("Scene file is cut short");
            int count = BinaryPrimitives.ReadInt32LittleEndian(data);
            if (count < 0 || count > 1024)
                throw new InvalidDataException($"Scene file has a bad part count {count}");

            var parts = new List<ScenePart>(count);
            int offset = 4;
            for (int p = 0; p < count; p++)
            {
                if (offset + PartHeaderSize > data.Length)
                    throw new InvalidDataException("Scene part header is cut short");
                var header = data.Slice(offset + 320);
                int width = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
                int height = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
                int offsetX = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
                int offsetY = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
                offset += PartHeaderSize;

                if (width < 0 || height < 0 || width > 1024 || height > 1024 ||
                    offset + width * height * CellSize > data.Length)
                    throw new InvalidDataException("Scene part cells are cut short");
                var cells = new SceneCell[width * height];
                for (int i = 0; i < cells.Length; i++, offset += CellSize)
                    cells[i] = new SceneCell(
                        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]) == 0,
                        (short)BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 8)..]));

                parts.Add(new ScenePart(width, height, offsetX, offsetY, cells));
            }

            return parts;
        }
    }
}

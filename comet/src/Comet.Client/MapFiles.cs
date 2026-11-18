namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Writes small client map files (GameMap.dat and DMap) for tests, in the 5619
    /// client's layout, so the server can run without a copy of the real client.
    /// </summary>
    public static class MapFiles
    {
        /// <summary>Builds a GameMap.dat listing maps and their file paths.</summary>
        /// <param name="maps">Map ID and path pairs, e.g. (1010, "map/map/newbie.7z")</param>
        public static byte[] GameMapDat(params (uint Id, string Path)[] maps)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((uint)maps.Length);
            foreach (var (id, path) in maps)
            {
                var bytes = Encoding.Latin1.GetBytes(path);
                writer.Write(id);
                writer.Write((uint)bytes.Length);
                writer.Write(bytes);
                writer.Write(256u);
            }

            return stream.ToArray();
        }

        /// <summary>Builds a DMap file.</summary>
        /// <param name="width">Map width in cells</param>
        /// <param name="height">Map height in cells</param>
        /// <param name="walkable">Which cells can be walked on (all if null)</param>
        /// <param name="elevation">Each cell's elevation (0 if null)</param>
        /// <param name="portals">Portal cells and their numbers</param>
        public static byte[] DMap(int width, int height,
            Func<int, int, bool> walkable = null,
            Func<int, int, short> elevation = null,
            IEnumerable<(ushort X, ushort Y, uint Index)> portals = null)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(1346456900u);
            writer.Write(0u);
            var puzzle = new byte[260];
            Encoding.ASCII.GetBytes("map\\puzzle\\test.pul").CopyTo(puzzle, 0);
            writer.Write(puzzle);
            writer.Write((uint)width);
            writer.Write((uint)height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    writer.Write((ushort)(walkable == null || walkable(x, y) ? 0 : 1));
                    writer.Write((ushort)0);
                    writer.Write(elevation?.Invoke(x, y) ?? 0);
                }

                writer.Write(0u);
            }

            var list = new List<(ushort X, ushort Y, uint Index)>(portals ?? Array.Empty<(ushort, ushort, uint)>());
            writer.Write((uint)list.Count);
            foreach (var (x, y, index) in list)
            {
                writer.Write((uint)x);
                writer.Write((uint)y);
                writer.Write(index);
            }

            writer.Write(0u);
            return stream.ToArray();
        }
    }
}

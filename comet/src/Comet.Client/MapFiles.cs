namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public static class MapFiles
    {
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

        public static byte[] DMap(int width, int height,
            Func<int, int, bool> walkable = null,
            Func<int, int, short> elevation = null,
            IEnumerable<(ushort X, ushort Y, uint Index)> portals = null,
            IEnumerable<(string Scene, int X, int Y)> terrain = null,
            int covers = 0)
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

            var objects = new List<(string Scene, int X, int Y)>(terrain ?? Array.Empty<(string, int, int)>());
            writer.Write((uint)(objects.Count + covers));
            for (int i = 0; i < covers; i++)
            {
                writer.Write(4u);
                writer.Write(Fixed("ani\\MapScene.ani", 260));
                writer.Write(Fixed("cover.tga", 128));
                for (int j = 0; j < 7; j++)
                    writer.Write(1);
            }

            foreach (var (scene, x, y) in objects)
            {
                writer.Write(1u);
                writer.Write(Fixed(scene, 260));
                writer.Write(x);
                writer.Write(y);
            }

            return stream.ToArray();
        }

        public static byte[] Scene(int width, int height, int offsetX = 0, int offsetY = 0,
            Func<int, int, bool> walkable = null, Func<int, int, int> altitude = null)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(1u);
            writer.Write(Fixed("ani\\MapScene.ani", 256));
            writer.Write(Fixed("part.tga", 64));
            writer.Write(-60);
            writer.Write(-48);
            writer.Write(200);
            writer.Write(width);
            writer.Write(height);
            writer.Write(0);
            writer.Write(offsetX);
            writer.Write(offsetY);
            writer.Write(0);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                writer.Write(walkable == null || walkable(x, y) ? 0 : 1);
                writer.Write(0);
                writer.Write(altitude?.Invoke(x, y) ?? 0);
            }

            return stream.ToArray();
        }

        private static byte[] Fixed(string text, int size)
        {
            var bytes = new byte[size];
            Encoding.Latin1.GetBytes(text).CopyTo(bytes, 0);
            return bytes;
        }
    }
}

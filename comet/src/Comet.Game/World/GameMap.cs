namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;

    public interface IMapObject
    {
        uint Identity { get; }
    }

    // Objects are kept in sectors one view range wide, so a view query only looks at the 3x3
    // sectors around a cell. Positions change only through Move, which keeps sectors right.
    public sealed class GameMap<T> where T : class, IMapObject
    {
        public const int ViewRange = 18;
        private const int SectorSize = ViewRange;

        private readonly object Lock = new object();
        private readonly Dictionary<uint, (T Object, ushort X, ushort Y)> Objects =
            new Dictionary<uint, (T, ushort, ushort)>();
        private readonly HashSet<T>[] Sectors;
        private readonly int SectorColumns;
        public uint Id { get; }
        public MapGrid Grid { get; }

        public GameMap(uint id, MapGrid grid)
        {
            this.Id = id;
            this.Grid = grid;
            this.SectorColumns = (grid.Width + SectorSize - 1) / SectorSize;
            int rows = (grid.Height + SectorSize - 1) / SectorSize;
            this.Sectors = new HashSet<T>[this.SectorColumns * rows];
        }

        public int Count
        {
            get { lock (this.Lock) return this.Objects.Count; }
        }

        public T Add(T obj, ushort x, ushort y)
        {
            this.CheckInside(x, y);
            lock (this.Lock)
            {
                T replaced = null;
                if (this.Objects.TryGetValue(obj.Identity, out var previous))
                {
                    this.SectorAt(previous.X, previous.Y).Remove(previous.Object);
                    replaced = previous.Object;
                }

                this.Objects[obj.Identity] = (obj, x, y);
                this.SectorAt(x, y).Add(obj);
                return replaced;
            }
        }

        public bool Remove(T obj)
        {
            lock (this.Lock)
            {
                if (!this.Objects.TryGetValue(obj.Identity, out var current) || current.Object != obj)
                    return false;
                this.Objects.Remove(obj.Identity);
                this.SectorAt(current.X, current.Y).Remove(obj);
                return true;
            }
        }

        public bool Move(T obj, ushort x, ushort y)
        {
            this.CheckInside(x, y);
            lock (this.Lock)
            {
                if (!this.Objects.TryGetValue(obj.Identity, out var current) || current.Object != obj)
                    return false;
                var from = this.SectorAt(current.X, current.Y);
                var to = this.SectorAt(x, y);
                if (from != to)
                {
                    from.Remove(obj);
                    to.Add(obj);
                }

                this.Objects[obj.Identity] = (obj, x, y);
                return true;
            }
        }

        public bool TryGetPosition(T obj, out ushort x, out ushort y)
        {
            lock (this.Lock)
            {
                if (this.Objects.TryGetValue(obj.Identity, out var current) && current.Object == obj)
                {
                    (x, y) = (current.X, current.Y);
                    return true;
                }

                (x, y) = (0, 0);
                return false;
            }
        }

        public List<T> InView(ushort x, ushort y, T except = null)
        {
            var found = new List<T>();
            int firstColumn = Math.Max(0, (x - ViewRange) / SectorSize);
            int lastColumn = Math.Min(this.SectorColumns - 1, (x + ViewRange) / SectorSize);
            int firstRow = Math.Max(0, (y - ViewRange) / SectorSize);
            int lastRow = Math.Min(this.Sectors.Length / this.SectorColumns - 1, (y + ViewRange) / SectorSize);

            lock (this.Lock)
            {
                for (int row = firstRow; row <= lastRow; row++)
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    var sector = this.Sectors[row * this.SectorColumns + column];
                    if (sector == null)
                        continue;
                    foreach (var obj in sector)
                    {
                        var position = this.Objects[obj.Identity];
                        if (obj != except && InView(x, y, position.X, position.Y))
                            found.Add(obj);
                    }
                }
            }

            return found;
        }

        public static bool InView(int x1, int y1, int x2, int y2) =>
            Math.Abs(x1 - x2) <= ViewRange && Math.Abs(y1 - y2) <= ViewRange;

        private HashSet<T> SectorAt(ushort x, ushort y) =>
            this.Sectors[(y / SectorSize) * this.SectorColumns + x / SectorSize] ??= new HashSet<T>();

        private void CheckInside(ushort x, ushort y)
        {
            if (!this.Grid.Contains(x, y))
                throw new ArgumentOutOfRangeException(nameof(x),
                    $"({x},{y}) is outside map {this.Id} ({this.Grid.Width}x{this.Grid.Height})");
        }
    }
}

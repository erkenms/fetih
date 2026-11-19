namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;

    /// <summary>Something that stands on a map: a player now, NPCs and monsters later.</summary>
    public interface IMapObject
    {
        /// <summary>Unique ID in the world (character ID for players).</summary>
        uint Identity { get; }
    }

    /// <summary>
    /// A running map: who is on it and where. Objects are kept in square sectors one view
    /// range wide, so finding everyone a player can see only looks at the 3x3 sectors
    /// around them instead of the whole map.
    /// </summary>
    /// <remarks>
    /// The map is the authority for positions it holds: movement goes through
    /// <see cref="Move"/>, which keeps the sectors right. All methods are thread-safe;
    /// queries return a snapshot.
    /// </remarks>
    /// <typeparam name="T">Object type (the game server's client)</typeparam>
    public sealed class GameMap<T> where T : class, IMapObject
    {
        /// <summary>
        /// How far a player sees: 18 cells, measured as the larger of the X and Y
        /// distances (5619: from reference, FieldOfView.ViewThreshold and
        /// MathHelper.RoughDistance; to confirm in VM).
        /// </summary>
        public const int ViewRange = 18;
        private const int SectorSize = ViewRange;

        private readonly object Lock = new object();
        private readonly Dictionary<uint, (T Object, ushort X, ushort Y)> Objects =
            new Dictionary<uint, (T, ushort, ushort)>();
        private readonly HashSet<T>[] Sectors;
        private readonly int SectorColumns;
        public uint Id { get; }
        public MapGrid Grid { get; }

        /// <summary>Creates an empty running map over a cell grid.</summary>
        public GameMap(uint id, MapGrid grid)
        {
            this.Id = id;
            this.Grid = grid;
            this.SectorColumns = (grid.Width + SectorSize - 1) / SectorSize;
            int rows = (grid.Height + SectorSize - 1) / SectorSize;
            this.Sectors = new HashSet<T>[this.SectorColumns * rows];
        }

        /// <summary>Number of objects on the map.</summary>
        public int Count
        {
            get { lock (this.Lock) return this.Objects.Count; }
        }

        /// <summary>
        /// Puts an object on the map at a cell. An object with the same identity already
        /// on the map is replaced (a player logging in again).
        /// </summary>
        /// <returns>The replaced object, or null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The cell is outside the map.</exception>
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

        /// <summary>
        /// Takes an object off the map if it is still the one on the map with its identity.
        /// </summary>
        /// <returns>True if it was removed.</returns>
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

        /// <summary>Moves an object that is on the map to another cell.</summary>
        /// <returns>False if the object isn't on the map.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The cell is outside the map.</exception>
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

        /// <summary>Where an object on the map stands.</summary>
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

        /// <summary>
        /// Everyone within view of a cell: at most <see cref="ViewRange"/> cells away on
        /// both axes.
        /// </summary>
        /// <param name="x">Cell X</param>
        /// <param name="y">Cell Y</param>
        /// <param name="except">An object to leave out (usually the one looking)</param>
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

        /// <summary>True if two cells are within view of each other.</summary>
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

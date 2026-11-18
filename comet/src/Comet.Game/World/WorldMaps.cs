namespace Comet.Game.World
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;

    /// <summary>
    /// The running maps of the world. A map starts running the first time something
    /// enters it, using its cell grid from the <see cref="MapStore"/>.
    /// </summary>
    /// <typeparam name="T">Object type (the game server's client)</typeparam>
    public sealed class WorldMaps<T> where T : class, IMapObject
    {
        // Fields and properties
        private readonly MapStore Store;
        private readonly ConcurrentDictionary<uint, Lazy<GameMap<T>>> Maps =
            new ConcurrentDictionary<uint, Lazy<GameMap<T>>>();

        /// <summary>Creates the world over a map store.</summary>
        public WorldMaps(MapStore store) => this.Store = store;

        /// <summary>
        /// Returns a running map, starting it if needed. Returns false if the map isn't
        /// listed or its cells can't be read.
        /// </summary>
        public bool TryGet(uint mapID, out GameMap<T> map)
        {
            map = null;
            if (!this.Store.Exists(mapID))
                return false;
            try
            {
                map = this.Maps.GetOrAdd(mapID, id => new Lazy<GameMap<T>>(
                    () => new GameMap<T>(id, this.Store.Get(id)),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                return true;
            }
            catch (Exception)
            {
                // MapStore logged why (map.load_failed)
                this.Maps.TryRemove(mapID, out _);
                return false;
            }
        }
    }
}

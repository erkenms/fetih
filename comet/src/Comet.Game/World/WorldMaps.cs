namespace Comet.Game.World
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;

    public sealed class WorldMaps<T> where T : class, IMapObject
    {
        private readonly MapStore Store;
        private readonly ConcurrentDictionary<uint, Lazy<GameMap<T>>> Maps =
            new ConcurrentDictionary<uint, Lazy<GameMap<T>>>();

        public WorldMaps(MapStore store) => this.Store = store;

        // Maps that have been opened so far
        public System.Collections.Generic.IEnumerable<GameMap<T>> Loaded =>
            this.Maps.Values.Where(m => m.IsValueCreated).Select(m => m.Value);

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

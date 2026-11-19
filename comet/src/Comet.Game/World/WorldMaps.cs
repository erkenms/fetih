namespace Comet.Game.World
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;

    public sealed class WorldMaps<T> where T : class, IMapObject
    {
        private readonly MapStore Store;
        private readonly ConcurrentDictionary<uint, Lazy<GameMap<T>>> Maps =
            new ConcurrentDictionary<uint, Lazy<GameMap<T>>>();

        public WorldMaps(MapStore store) => this.Store = store;

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

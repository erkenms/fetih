namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    // Removal only takes out the given instance, as in SessionRegistry.
    public sealed class PlayerRegistry<T> where T : class
    {
        private readonly SessionRegistry<T> ById = new SessionRegistry<T>();
        private readonly ConcurrentDictionary<string, T> ByName =
            new ConcurrentDictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        public int Count => this.ById.Count;

        public ICollection<T> Values => this.ById.Values;

        public T Add(uint characterID, string name, T player)
        {
            this.ByName[name] = player;
            return this.ById.Replace(characterID, player);
        }

        public bool Remove(uint characterID, string name, T player)
        {
            this.ByName.TryRemove(new KeyValuePair<string, T>(name, player));
            return this.ById.Remove(characterID, player);
        }

        public bool TryGet(uint characterID, out T player) =>
            this.ById.TryGet(characterID, out player);

        public bool TryGet(string name, out T player) =>
            this.ByName.TryGetValue(name, out player);
    }
}

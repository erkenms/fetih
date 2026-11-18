namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// The players in the game world, by character ID and by name (names match regardless
    /// of case, like the name column). A player is added when their character enters the
    /// game and removed when that connection ends. As in <see cref="SessionRegistry{T}"/>,
    /// removal only takes out the given instance, so a replaced player's teardown never
    /// removes the player who replaced them.
    /// </summary>
    /// <typeparam name="T">Player type (the game server's client)</typeparam>
    public sealed class PlayerRegistry<T> where T : class
    {
        private readonly SessionRegistry<T> ById = new SessionRegistry<T>();
        private readonly ConcurrentDictionary<string, T> ByName =
            new ConcurrentDictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Number of players in the world.</summary>
        public int Count => this.ById.Count;

        /// <summary>Snapshot of all players in the world.</summary>
        public ICollection<T> Values => this.ById.Values;

        /// <summary>
        /// Adds a player, replacing anyone registered with the same character.
        /// </summary>
        /// <param name="characterID">Character ID of the player</param>
        /// <param name="name">Character name of the player</param>
        /// <param name="player">The player</param>
        /// <returns>The replaced player, or null.</returns>
        public T Add(uint characterID, string name, T player)
        {
            this.ByName[name] = player;
            return this.ById.Replace(characterID, player);
        }

        /// <summary>Removes a player if they are still the registered instance.</summary>
        /// <returns>True if the player was removed.</returns>
        public bool Remove(uint characterID, string name, T player)
        {
            this.ByName.TryRemove(new KeyValuePair<string, T>(name, player));
            return this.ById.Remove(characterID, player);
        }

        /// <summary>Finds a player by character ID.</summary>
        public bool TryGet(uint characterID, out T player) =>
            this.ById.TryGet(characterID, out player);

        /// <summary>Finds a player by character name, ignoring case.</summary>
        public bool TryGet(string name, out T player) =>
            this.ByName.TryGetValue(name, out player);
    }
}

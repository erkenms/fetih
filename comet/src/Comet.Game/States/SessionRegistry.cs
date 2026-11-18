namespace Comet.Game.States
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// Thread-safe registry of connected sessions keyed by account ID. Only one session
    /// may exist per account. Removal is conditional on the session instance, so a stale
    /// session being torn down can never evict the session that replaced it.
    /// </summary>
    /// <typeparam name="T">Session type (the game server's client)</typeparam>
    public sealed class SessionRegistry<T> where T : class
    {
        private readonly ConcurrentDictionary<uint, T> Sessions = new ConcurrentDictionary<uint, T>();

        /// <summary>Number of registered sessions.</summary>
        public int Count => this.Sessions.Count;

        /// <summary>Snapshot of all registered sessions.</summary>
        public ICollection<T> Values => this.Sessions.Values;

        /// <summary>
        /// Registers a session for an account, replacing any existing session for that
        /// account atomically.
        /// </summary>
        /// <returns>The replaced session, or null if the account had none.</returns>
        public T Replace(uint accountID, T session)
        {
            while (true)
            {
                if (this.Sessions.TryGetValue(accountID, out T previous))
                {
                    if (this.Sessions.TryUpdate(accountID, session, previous))
                        return previous;
                }
                else if (this.Sessions.TryAdd(accountID, session))
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Removes the account's session only if it is still the given instance.
        /// </summary>
        /// <returns>True if the session was removed.</returns>
        public bool Remove(uint accountID, T session) =>
            this.Sessions.TryRemove(new KeyValuePair<uint, T>(accountID, session));

        /// <summary>Gets the session registered for an account.</summary>
        public bool TryGet(uint accountID, out T session) =>
            this.Sessions.TryGetValue(accountID, out session);
    }
}

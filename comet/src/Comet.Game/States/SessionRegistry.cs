namespace Comet.Game.States
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    // Removal only takes out the given instance, so a stale session being torn down can't
    // evict the one that replaced it.
    public sealed class SessionRegistry<T> where T : class
    {
        private readonly ConcurrentDictionary<uint, T> Sessions = new ConcurrentDictionary<uint, T>();

        public int Count => this.Sessions.Count;

        public ICollection<T> Values => this.Sessions.Values;

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

        public bool Remove(uint accountID, T session) =>
            this.Sessions.TryRemove(new KeyValuePair<uint, T>(accountID, session));

        public bool TryGet(uint accountID, out T session) =>
            this.Sessions.TryGetValue(accountID, out session);
    }
}

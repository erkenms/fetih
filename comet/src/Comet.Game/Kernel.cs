namespace Comet.Game
{
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Services;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Runtime.Caching;
    using System.Threading.Tasks;

    public static class Kernel
    {
        // Clients holds every authenticated connection by account,
        // including ones still creating a character.
        public static SessionRegistry<Client> Clients = new SessionRegistry<Client>();

        // Characters in the game world, by character ID and name
        public static PlayerRegistry<Client> Players = new PlayerRegistry<Client>();

        // Map grids, read from the client's map files at startup
        public static MapStore Maps;

        // Running maps and who is on them
        public static WorldMaps<Client> World;

        public static MemoryCache Logins = MemoryCache.Default;
        public static ConcurrentDictionary<uint, byte> Registration = new ConcurrentDictionary<uint, byte>();

        public static class Services
        {
            public static RandomnessService Randomness = new RandomnessService();
        }

        /// <summary>Returns the next random number from the generator.</summary>
        /// <param name="minValue">The least legal value for the Random number.</param>
        /// <param name="maxValue">One greater than the greatest legal return value.</param>
        public static Task<int> NextAsync(int minValue, int maxValue) => 
            Services.Randomness.NextAsync(minValue, maxValue);

        /// <summary>Writes random numbers from the generator to a buffer.</summary>
        public static Task NextBytesAsync(byte[] buffer) =>
            Services.Randomness.NextBytesAsync(buffer);
    }
}
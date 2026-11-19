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

        public static PlayerRegistry<Client> Players = new PlayerRegistry<Client>();

        public static MapStore Maps;

        public static WorldMaps<Client> World;

        public static MemoryCache Logins = MemoryCache.Default;
        public static ConcurrentDictionary<uint, byte> Registration = new ConcurrentDictionary<uint, byte>();

        public static class Services
        {
            public static RandomnessService Randomness = new RandomnessService();
        }

        public static Task<int> NextAsync(int minValue, int maxValue) => 
            Services.Randomness.NextAsync(minValue, maxValue);

        public static Task NextBytesAsync(byte[] buffer) =>
            Services.Randomness.NextBytesAsync(buffer);
    }
}
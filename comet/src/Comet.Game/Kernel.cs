namespace Comet.Game
{
    using Comet.Game.Items;
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

        // Items lying on each map, kept apart from the players
        public static WorldMaps<GroundItem> Ground;

        // Where portal cells lead, read from the database at startup
        public static PortalTable Portals = new PortalTable(new Database.Models.DbPortal[0]);

        // Experience to reach each next level, read from the database at startup
        public static ExperienceTable Experience = new ExperienceTable(new Database.Models.DbLevelExp[0]);

        // Item types read from the client's itemtype.dat at startup
        public static ItemTypeTable ItemTypes = new ItemTypeTable(new ItemType[0]);

        // NPCs on each map, read from Data/npcs.json at startup
        public static NpcTable Npcs = new NpcTable(new Npc[0]);

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
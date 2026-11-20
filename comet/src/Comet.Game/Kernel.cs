namespace Comet.Game
{
    using Comet.Game.Items;
    using Comet.Game.Npcs;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Services;
    using System;
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

        // Where portal cells lead, read from Data/portals.json at startup
        public static PortalTable Portals = new PortalTable(new Portal[0]);

        // Map type flags, read from Data/maps.json at startup
        public static MapTypes MapTypes = new MapTypes(new MapType[0]);

        // Experience to reach each next level, read from Data/levels.json at startup
        public static ExperienceTable Experience = new ExperienceTable(new LevelExp[0]);

        // Item types read from the client's itemtype.dat at startup
        public static ItemTypeTable ItemTypes = new ItemTypeTable(new ItemType[0]);

        // Spells and weapon proficiency levels, read from the client's magictype.dat and
        // WeaponSkillLevelExp.ini at startup
        public static Magic.MagicTypeTable Magics = new Magic.MagicTypeTable(new Magic.MagicType[0]);
        public static Magic.ProficiencyTable Proficiencies = new Magic.ProficiencyTable(new (ushort, uint)[0]);

        // NPCs on each map, read from Data/npcs.json at startup
        public static NpcTable Npcs = new NpcTable(new Npc[0]);

        // NPCs that teleport for a fare, read from Data/teleporters.json at startup
        public static Teleporters Teleporters = new Teleporters(new Teleporter[0]);

        // What shopkeepers sell, read from the client's Shop.dat at startup
        public static ShopTable Shops = new ShopTable(new Shop[0]);

        // Monster types from Data/monsters.json and the monsters on each map, from the
        // generators in the spawn file
        public static MonsterTypeTable MonsterTypes = new MonsterTypeTable(new MonsterType[0]);
        public static WorldMaps<Monster> Monsters;

        // Runs work on the player's own packet queue, after the packets before it. The server
        // sets this; without one (in unit tests) the work runs at once.
        public static Action<Client, Func<Task>> Post = (client, work) => _ = work();

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
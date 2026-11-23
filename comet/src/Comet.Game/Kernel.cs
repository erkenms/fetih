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
        // Every authenticated connection by account, including ones still creating a character
        public static SessionRegistry<Client> Clients = new SessionRegistry<Client>();

        public static PlayerRegistry<Client> Players = new PlayerRegistry<Client>();

        // Logouts still saving, by account: a new login for the account waits for its own
        public static ConcurrentDictionary<uint, Task> Logouts = new ConcurrentDictionary<uint, Task>();

        public static MapStore Maps;

        public static WorldMaps<Client> World;

        public static WorldMaps<GroundItem> Ground;

        public static PortalTable Portals = new PortalTable(new Portal[0]);

        public static MapTypes MapTypes = new MapTypes(new MapType[0]);

        public static ExperienceTable Experience = new ExperienceTable(new LevelExp[0]);

        public static ItemTypeTable ItemTypes = new ItemTypeTable(new ItemType[0]);

        // What "+N" adds to items
        public static ItemAdditionTable ItemAdditions = new ItemAdditionTable(new ItemAddition[0]);

        public static Magic.MagicTypeTable Magics = new Magic.MagicTypeTable(new Magic.MagicType[0]);
        public static Magic.MagicEffectTable MagicEffects = new Magic.MagicEffectTable(new());
        public static Magic.ProficiencyTable Proficiencies = new Magic.ProficiencyTable(new (ushort, uint)[0]);
        public static Magic.MagicTypeOpTable MagicOps =
            new Magic.MagicTypeOpTable(new (byte, byte, byte, Magic.MagicTypeOpTable.Operation, ushort[])[0]);

        public static NpcTable Npcs = new NpcTable(new Npc[0]);

        public static Teleporters Teleporters = new Teleporters(new Teleporter[0]);

        public static ShopTable Shops = new ShopTable(new Shop[0]);
        public static Arena.HonorRewards HonorRewards = new Arena.HonorRewards(new uint[0]);

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
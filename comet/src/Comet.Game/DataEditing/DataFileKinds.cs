namespace Comet.Game.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Comet.Game.Combat;
    using Comet.Game.Npcs;
    using Comet.Game.States;
    using Comet.Game.World;

    public sealed record DataFileKind(string FileName, string ArrayKey, Type EntryType)
    {
        public DataFile Parse(string text) => this.Create(f => f.GetMethod("Parse"), text);

        public DataFile Load(string path) => this.Create(f => f.GetMethod("Load"), path);

        private DataFile Create(Func<Type, System.Reflection.MethodInfo> method, string argument)
        {
            var type = typeof(DataFile<>).MakeGenericType(this.EntryType);
            try
            {
                return (DataFile)method(type).Invoke(null, new object[] { argument, this.ArrayKey });
            }
            catch (System.Reflection.TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }

    // The files under Comet.Game/Data and the record each entry reads into
    public static class DataFileKinds
    {
        public static readonly DataFileKind Npcs = new DataFileKind("npcs.json", "npcs", typeof(Npc));
        public static readonly DataFileKind Goals = new DataFileKind("goals.json", "npcs", typeof(Npc));
        public static readonly DataFileKind Maps = new DataFileKind("maps.json", "maps", typeof(MapType));
        public static readonly DataFileKind Portals = new DataFileKind("portals.json", "portals", typeof(Portal));
        public static readonly DataFileKind Spawns = new DataFileKind("spawns.json", "spawns", typeof(Generator));
        public static readonly DataFileKind Monsters = new DataFileKind("monsters.json", "monsters", typeof(MonsterType));
        public static readonly DataFileKind Levels = new DataFileKind("levels.json", "levels", typeof(LevelExp));
        public static readonly DataFileKind Talkers = new DataFileKind("talkers.json", "talkers", typeof(Talker));
        public static readonly DataFileKind Teleporters = new DataFileKind("teleporters.json", "teleporters", typeof(Teleporter));
        public static readonly DataFileKind Bosses = new DataFileKind("bosses.json", "bosses", typeof(BossSpawn));
        public static readonly DataFileKind Drops = new DataFileKind("drops.json", "drops", typeof(MonsterDrops));

        public static readonly IReadOnlyList<DataFileKind> All = new[]
        {
            Npcs, Goals, Maps, Portals, Spawns, Monsters, Levels, Talkers, Teleporters, Bosses, Drops,
        };

        public static DataFileKind Find(string fileName) =>
            All.FirstOrDefault(k => string.Equals(k.FileName, Path.GetFileName(fileName), StringComparison.OrdinalIgnoreCase));
    }
}

namespace Comet.Game.World
{
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    // NPCs by map. They don't move, and a map has a few dozen at most, so a view query
    // looks at each NPC of the map.
    public sealed class NpcTable
    {
        private readonly Dictionary<uint, List<DbNpc>> ByMap;

        public NpcTable(IEnumerable<DbNpc> npcs)
        {
            this.ByMap = npcs.GroupBy(n => n.MapID).ToDictionary(g => g.Key, g => g.ToList());
        }

        public int Count => this.ByMap.Values.Sum(l => l.Count);

        public IEnumerable<uint> MapIDs => this.ByMap.Keys;

        public IEnumerable<DbNpc> InView(uint mapID, int x, int y) =>
            this.ByMap.TryGetValue(mapID, out var npcs)
                ? npcs.Where(n => GameMap<States.Client>.InView(x, y, n.X, n.Y))
                : Enumerable.Empty<DbNpc>();

        public DbNpc Find(uint mapID, uint npcID) =>
            this.ByMap.TryGetValue(mapID, out var npcs) ? npcs.FirstOrDefault(n => n.NpcID == npcID) : null;
    }
}

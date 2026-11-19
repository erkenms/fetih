namespace Comet.Game.World
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    // NPCs by map. They don't move, and a map has a few dozen at most, so a view query
    // looks at each NPC of the map.
    public sealed class NpcTable
    {
        private readonly Dictionary<uint, List<Npc>> ByMap;

        private sealed record NpcFile(List<Npc> Npcs);

        // Data/npcs.json; an NPC ID listed twice is an error
        public static List<Npc> Load(string path)
        {
            var npcs = DataFiles.Read<NpcFile>(path).Npcs ?? new List<Npc>();
            var repeated = npcs.GroupBy(n => n.NpcID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these NPC IDs more than once: {string.Join(", ", repeated)}");
            return npcs;
        }

        public NpcTable(IEnumerable<Npc> npcs)
        {
            this.ByMap = npcs.GroupBy(n => n.MapID).ToDictionary(g => g.Key, g => g.ToList());
        }

        public int Count => this.ByMap.Values.Sum(l => l.Count);

        public IEnumerable<uint> MapIDs => this.ByMap.Keys;

        public IEnumerable<Npc> InView(uint mapID, int x, int y) =>
            this.ByMap.TryGetValue(mapID, out var npcs)
                ? npcs.Where(n => GameMap<States.Client>.InView(x, y, n.X, n.Y))
                : Enumerable.Empty<Npc>();

        public Npc Find(uint mapID, uint npcID) =>
            this.ByMap.TryGetValue(mapID, out var npcs) ? npcs.FirstOrDefault(n => n.NpcID == npcID) : null;
    }
}

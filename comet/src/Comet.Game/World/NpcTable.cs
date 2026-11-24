namespace Comet.Game.World
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    // NPCs by map. They don't move, and a map has a few dozen, the training ground's goals
    // a thousand, so a view query looks at each NPC of the map.
    public sealed class NpcTable
    {
        private readonly Dictionary<uint, List<Npc>> ByMap;

        private sealed record NpcFile(List<Npc> Npcs);

        // NPC files share one set of IDs
        public static List<Npc> Load(params string[] paths)
        {
            var npcs = paths.SelectMany(p => DataFiles.Read<NpcFile>(p).Npcs ?? new List<Npc>()).ToList();
            string path = string.Join(" and ", paths);
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

        public IReadOnlyList<Npc> On(uint mapID) =>
            this.ByMap.TryGetValue(mapID, out var npcs) ? npcs : new List<Npc>();

        public IEnumerable<Npc> All => this.ByMap.Values.SelectMany(l => l);

        // NPCs gone or changed in the next table, and NPCs new or changed in it
        public (List<Npc> Gone, List<Npc> Shown) Changes(NpcTable next)
        {
            var before = this.All.ToDictionary(n => n.NpcID);
            var after = next.All.ToDictionary(n => n.NpcID);
            var gone = before.Values.Where(n => !after.TryGetValue(n.NpcID, out var now) || !Npc.Same(n, now)).ToList();
            var shown = after.Values.Where(n => !before.TryGetValue(n.NpcID, out var was) || !Npc.Same(was, n)).ToList();
            return (gone, shown);
        }

        public Npc Find(uint mapID, uint npcID) =>
            this.ByMap.TryGetValue(mapID, out var npcs) ? npcs.FirstOrDefault(n => n.NpcID == npcID) : null;
    }
}

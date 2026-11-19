namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    // A player's items in each warehouse, by the warehouseman's NPC ID. Every city's
    // warehouse holds its own items.
    public sealed class Warehouses
    {
        public const int Capacity = 60;

        private readonly Dictionary<uint, List<DbItem>> ByNpc;

        public Warehouses(IEnumerable<DbItem> items)
        {
            this.ByNpc = items.GroupBy(i => i.Package).ToDictionary(g => g.Key, g => g.ToList());
        }

        public IReadOnlyList<DbItem> In(uint npcID) =>
            this.ByNpc.TryGetValue(npcID, out var items) ? items : new List<DbItem>();

        public bool TryAdd(uint npcID, DbItem item)
        {
            if (!this.ByNpc.TryGetValue(npcID, out var items))
                this.ByNpc[npcID] = items = new List<DbItem>();
            if (items.Count >= Capacity)
                return false;
            items.Add(item);
            return true;
        }

        public DbItem Find(uint npcID, uint itemID) => this.In(npcID).FirstOrDefault(i => i.ItemID == itemID);

        public bool Remove(uint npcID, DbItem item) =>
            this.ByNpc.TryGetValue(npcID, out var items) && items.Remove(item);
    }
}

namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    // A trade changes both players' inventories from one player's packet queue, so every
    // access takes the lock.
    public sealed class Inventory
    {
        public const int Capacity = 40;

        private readonly List<DbItem> Contents;

        public Inventory(IEnumerable<DbItem> items)
        {
            this.Contents = items.ToList();
        }

        public IReadOnlyList<DbItem> Items
        {
            get { lock (this.Contents) return this.Contents.ToList(); }
        }

        public int Count
        {
            get { lock (this.Contents) return this.Contents.Count; }
        }

        public int FreeSlots => Capacity - this.Count;

        public bool TryAdd(DbItem item)
        {
            lock (this.Contents)
            {
                if (this.Contents.Count >= Capacity)
                    return false;
                this.Contents.Add(item);
                return true;
            }
        }

        // Past the capacity too: for items that already belong to the player and must not be lost
        public void Add(DbItem item)
        {
            lock (this.Contents)
                this.Contents.Add(item);
        }

        public bool Remove(DbItem item)
        {
            lock (this.Contents)
                return this.Contents.Remove(item);
        }

        public DbItem Find(uint itemID)
        {
            lock (this.Contents)
                return this.Contents.FirstOrDefault(i => i.ItemID == itemID);
        }
    }

    public enum ItemPosition : byte
    {
        Inventory = 0,
        Headwear = 1,
        Necklace = 2,
        Armor = 3,
        RightHand = 4,
        LeftHand = 5,
        Ring = 6,
        Bottle = 7,
        Boots = 8,
        Garment = 9,
        Steed = 12,
        // The riding crop (Albetros 5517: SteedTalisman)
        Crop = 18,
        Warehouse = 230,
        Ground = 254,
    }
}

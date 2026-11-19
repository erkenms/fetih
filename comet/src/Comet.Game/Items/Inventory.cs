namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    public sealed class Inventory
    {
        public const int Capacity = 40;

        private readonly List<DbItem> Contents;

        public Inventory(IEnumerable<DbItem> items)
        {
            this.Contents = items.ToList();
        }

        public IReadOnlyList<DbItem> Items => this.Contents;

        public int Count => this.Contents.Count;

        public int FreeSlots => Capacity - this.Contents.Count;

        public bool TryAdd(DbItem item)
        {
            if (this.Contents.Count >= Capacity)
                return false;
            this.Contents.Add(item);
            return true;
        }

        public DbItem Find(uint itemID) => this.Contents.FirstOrDefault(i => i.ItemID == itemID);
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
    }
}

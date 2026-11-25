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

        // The whole item, all of its stack; spending one goes through ItemActions.SpendOneAsync
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

        public bool Contains(DbItem item)
        {
            lock (this.Contents)
                return this.Contents.Contains(item);
        }

        // How many of the type, counting every item in a stack
        public int CountOf(uint typeID)
        {
            lock (this.Contents)
                return this.Contents.Where(i => i.TypeID == typeID).Sum(i => (int)i.StackAmount);
        }

        // Free slots needed for count more of the type, after topping up its stacks
        public int SlotsFor(uint typeID, int count, ushort stackLimit)
        {
            if (stackLimit <= 1)
                return count;
            lock (this.Contents)
            {
                int room = this.Contents.Where(i => IsStackOf(i, typeID, stackLimit)).Sum(i => stackLimit - i.StackAmount);
                return count <= room ? 0 : (count - room + stackLimit - 1) / stackLimit;
            }
        }

        // One more on a stack of the type with room; null if none has room
        public DbItem StackOne(uint typeID, ushort stackLimit)
        {
            lock (this.Contents)
            {
                var stack = this.Contents.FirstOrDefault(i => IsStackOf(i, typeID, stackLimit));
                if (stack != null)
                    stack.StackAmount++;
                return stack;
            }
        }

        // One of the item: a stack gives one up, a single item leaves the inventory
        public bool TakeOne(DbItem item)
        {
            lock (this.Contents)
            {
                if (!this.Contents.Contains(item))
                    return false;
                if (item.StackAmount > 1)
                    item.StackAmount--;
                else
                    this.Contents.Remove(item);
                return true;
            }
        }

        // count of the type, from its stacks in order; the items taken from, or null if there
        // are fewer
        public List<DbItem> Take(uint typeID, int count)
        {
            lock (this.Contents)
            {
                var items = this.Contents.Where(i => i.TypeID == typeID).ToList();
                if (items.Sum(i => (int)i.StackAmount) < count)
                    return null;
                var taken = new List<DbItem>();
                foreach (var item in items)
                {
                    if (count == 0)
                        break;
                    int n = System.Math.Min(count, (int)item.StackAmount);
                    if (n == item.StackAmount)
                        this.Contents.Remove(item);
                    else
                        item.StackAmount -= (ushort)n;
                    count -= n;
                    taken.Add(item);
                }
                return taken;
            }
        }

        // amount of the stack's items move to part, a new item in a free slot
        public bool Split(DbItem stack, DbItem part, ushort amount)
        {
            lock (this.Contents)
            {
                if (!this.Contents.Contains(stack) || amount == 0 || amount >= stack.StackAmount ||
                    this.Contents.Count >= Capacity)
                    return false;
                stack.StackAmount -= amount;
                part.StackAmount = amount;
                this.Contents.Add(part);
                return true;
            }
        }

        private static bool IsStackOf(DbItem item, uint typeID, ushort stackLimit) =>
            item.TypeID == typeID && item.StackAmount < stackLimit && !item.Locked;
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
        // The riding crop
        Crop = 18,
        Warehouse = 230,
        Detained = 250,
        Ground = 254,
    }
}

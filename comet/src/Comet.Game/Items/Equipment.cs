namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    public sealed class Equipment
    {
        private readonly Dictionary<ItemPosition, DbItem> Slots = new Dictionary<ItemPosition, DbItem>();

        public Equipment(IEnumerable<DbItem> items)
        {
            foreach (var item in items)
                this.Slots[(ItemPosition)item.Position] = item;
        }

        public IEnumerable<DbItem> Items => this.Slots.Values.OrderBy(i => i.Position);

        public DbItem this[ItemPosition position] =>
            this.Slots.TryGetValue(position, out var item) ? item : null;

        public uint TypeAt(ItemPosition position) => this[position]?.TypeID ?? 0;

        public byte ColorAt(ItemPosition position) => this[position]?.Color ?? 0;

        public void Set(ItemPosition position, DbItem item) => this.Slots[position] = item;

        public void Clear(ItemPosition position) => this.Slots.Remove(position);

        public static bool IsEquipped(byte position) =>
            position >= (byte)ItemPosition.Headwear && position <= (byte)ItemPosition.Garment ||
            position == (byte)ItemPosition.Steed || position == (byte)ItemPosition.Crop;
    }
}

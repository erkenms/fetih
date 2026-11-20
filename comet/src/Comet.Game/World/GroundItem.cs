namespace Comet.Game.World
{
    using System;
    using System.Threading;
    using Comet.Game.Database.Models;

    // An item or a pile of silver lying on a map
    public sealed class GroundItem : IMapObject
    {
        private static int LastID;

        public uint Identity { get; } = (uint)Interlocked.Increment(ref LastID);
        public uint MapID { get; init; }
        public ushort X { get; init; }
        public ushort Y { get; init; }
        public uint TypeID { get; init; }
        public DateTime ExpiresAt { get; init; }

        // The item, or null for silver
        public DbItem Item { get; init; }
        public uint Silver { get; init; }

        // Loot is only the killer's to pick up until OwnedUntil; 0 for anyone's
        public uint OwnerID { get; init; }
        public DateTime OwnedUntil { get; init; }

        public bool MayPickUp(uint characterID, DateTime now) =>
            this.OwnerID == 0 || this.OwnerID == characterID || now >= this.OwnedUntil;

        public static uint SilverType(uint amount) =>
            amount > 10000 ? 1091020u :
            amount > 5000 ? 1091010u :
            amount > 1000 ? 1091000u :
            amount > 100 ? 1090020u :
            amount > 50 ? 1090010u : 1090000u;
    }
}

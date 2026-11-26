namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Refinery states of the player's own items: the count at 4, then 28 bytes each from 8:
    // item ID, mode, kind (RefineryKind), level, power, seconds left, and 4 bytes the client
    // doesn't read. The client finds the item among its inventory and gear, so it goes after
    // the item's MsgItemInfo, and again after each one: those replace the client's item.
    public sealed class MsgItemStatus : MsgBase<Client>
    {
        public const int EntrySize = 28;

        public List<Entry> Entries { get; } = new List<Entry>();

        public MsgItemStatus() => base.Type = PacketType.MsgItemStatus;

        public MsgItemStatus(params Entry[] entries) : this() => this.Entries.AddRange(entries);

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Entries.Count);     // 4
            foreach (var entry in this.Entries)         // 8
            {
                writer.Write(entry.ItemID);
                writer.Write((uint)entry.Mode);
                writer.Write(entry.Kind);
                writer.Write(entry.Level);
                writer.Write(entry.Power);
                writer.Write(entry.Seconds);
                writer.Write(0u);
            }
            return writer.ToArray();
        }

        public enum StatusMode : uint
        {
            // Sets the temporary state, its time counting from the packet
            Refine = 0,
            // Clears the temporary state; a permanent one shows again
            Expire = 1,
            // Sets the permanent state, leaving the temporary one
            Stable = 3,
            // Clears the temporary state and sets the permanent one, and refreshes the
            // stabilization window
            Stabilized = 4,
        }

        public readonly record struct Entry(uint ItemID, StatusMode Mode, uint Kind, uint Level, uint Power, uint Seconds);
    }
}

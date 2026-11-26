namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Updates days elapsed or removes entries from the client's detained (Page 0) or
    // reward (Page 1) list and refreshes the open dialog.
    public sealed class MsgDetainItemUpdate : MsgBase<Client>
    {
        public const uint DetainPage = 0;
        public const uint RewardPage = 1;

        public uint Page { get; set; }
        public List<Entry> Entries { get; } = new List<Entry>();

        public MsgDetainItemUpdate(uint page)
        {
            base.Type = PacketType.MsgDetainItemUpdate;
            this.Page = page;
        }

        public static MsgDetainItemUpdate Remove(uint page, uint detainID)
        {
            var msg = new MsgDetainItemUpdate(page);
            msg.Entries.Add(new Entry(detainID, 0, Keep: false));
            return msg;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Page);               // 4
            writer.Write(this.Entries.Count);      // 8
            foreach (var entry in this.Entries)    // 12 bytes each
            {
                writer.Write(entry.DetainID);
                writer.Write(entry.Days);
                writer.Write((byte)(entry.Keep ? 1 : 0));
                writer.Write((byte)(entry.ShowCard ? 1 : 0));
                writer.Write((ushort)0);
            }
            return writer.ToArray();
        }

        public readonly record struct Entry(uint DetainID, uint Days, bool Keep, bool ShowCard = false);
    }
}

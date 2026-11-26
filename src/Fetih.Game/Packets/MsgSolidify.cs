namespace Fetih.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The stabilization window's button (CDlgSolidify): the mode at 4 (ushort; 0 stabilizes a
    // refinery), the gear at 8, the count of Permanent Stones at 12 and their item IDs from
    // 16. Nothing answers it but MsgItemStatus.
    public sealed class MsgSolidify : MsgBase<Client>
    {
        public const ushort RefineryMode = 0;
        public const int MaxStones = 40;

        public ushort Mode { get; set; }
        public uint EquipmentID { get; set; }
        public uint[] StoneIDs { get; set; } = new uint[0];

        public MsgSolidify() => base.Type = PacketType.MsgSolidify;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Mode = reader.ReadUInt16();
            reader.ReadUInt16();
            this.EquipmentID = reader.ReadUInt32();
            uint count = reader.ReadUInt32();
            int available = Math.Max(0, (bytes.Length - 16) / 4);
            this.StoneIDs = new uint[(int)Math.Min(Math.Min(count, (uint)available), MaxStones)];
            for (int i = 0; i < this.StoneIDs.Length; i++)
                this.StoneIDs[i] = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Mode);
            writer.Write((ushort)0);
            writer.Write(this.EquipmentID);
            writer.Write((uint)this.StoneIDs.Length);
            foreach (var id in this.StoneIDs)
                writer.Write(id);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => this.Mode == RefineryMode
            ? Items.Refinery.StabilizeAsync(client, this.EquipmentID, this.StoneIDs, DateTime.UtcNow)
            : Items.Refinery.RejectAsync(client, this.EquipmentID, "mode_" + this.Mode);
    }
}

namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The 5619 client sends it once the player confirms using a refinery item on an item:
    // using the refinery item turns the cursor into a picker, and clicking gear asks to
    // confirm. The mode at 4 (ushort; 0 refines), the gear at 8, the refinery item at 12.
    // Nothing answers it but MsgItemStatus.
    public sealed class MsgQuench : MsgBase<Client>
    {
        public const ushort RefineMode = 0;

        public ushort Mode { get; set; }
        public uint TargetID { get; set; }
        public uint RefineryItemID { get; set; }

        public MsgQuench() => base.Type = PacketType.MsgQuench;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Mode = reader.ReadUInt16();
            reader.ReadUInt16();
            this.TargetID = reader.ReadUInt32();
            this.RefineryItemID = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Mode);
            writer.Write((ushort)0);
            writer.Write(this.TargetID);
            writer.Write(this.RefineryItemID);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => this.Mode == RefineMode
            ? Items.Refinery.RefineAsync(client, this.TargetID, this.RefineryItemID, System.DateTime.UtcNow)
            : Items.Refinery.RejectAsync(client, this.TargetID, "mode_" + this.Mode);
    }
}

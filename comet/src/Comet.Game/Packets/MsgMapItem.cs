namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    // An item on the ground: shown, removed, or asked for by a player picking it up
    public sealed class MsgMapItem : MsgBase<Client>
    {
        public uint ID { get; set; }
        public uint TypeID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Color { get; set; }
        public MapItemAction Action { get; set; }

        public MsgMapItem() => base.Type = PacketType.MsgMapItem;

        public MsgMapItem(GroundItem item, MapItemAction action) : this()
        {
            this.ID = item.Identity;
            this.TypeID = item.TypeID;
            this.X = item.X;
            this.Y = item.Y;
            this.Color = item.Item?.Color ?? 3;
            this.Action = action;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.ID = reader.ReadUInt32();
            this.TypeID = reader.ReadUInt32();
            this.X = reader.ReadUInt16();
            this.Y = reader.ReadUInt16();
            this.Color = reader.ReadUInt16();
            this.Action = (MapItemAction)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.ID);                  // 4
            writer.Write(this.TypeID);              // 8
            writer.Write(this.X);                   // 12
            writer.Write(this.Y);                   // 14
            writer.Write(this.Color);               // 16
            writer.Write((ushort)this.Action);      // 18
            writer.Write(0u);                       // 20
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client)
        {
            if (this.Action == MapItemAction.Pick)
                return Ground.PickUpAsync(client, this.ID);
            LogEvents.For<MsgMapItem>(LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                this.Type, this.Action, this.Length);
            return Task.CompletedTask;
        }

        public enum MapItemAction : ushort
        {
            Create = 1,
            Delete = 2,
            Pick = 3,
        }
    }
}

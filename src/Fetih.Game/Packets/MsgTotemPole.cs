namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Guild arsenal actions (2203, 28 bytes): action (u16) at 4, totem type (i32) at 8,
    // item ID (u32) at 12, enhancement BP (i32) at 16, reserved (u32) at 20, enhancement
    // expiration YYYYMMDD (u32) at 24.
    public sealed class MsgTotemPole : MsgBase<Client>
    {
        public const int Size = 28;

        public enum TotemPoleAction : ushort
        {
            Unlock = 0,
            Inscribe = 1,
            Remove = 2,
            Enhance = 3,
            Refresh = 4,
        }

        public TotemPoleAction Action { get; set; }
        public uint TotemType { get; set; }
        public uint ItemID { get; set; }
        public uint Enhancement { get; set; }
        public uint Reserved { get; set; }
        public uint EnhancementExpiration { get; set; }

        public MsgTotemPole() => base.Type = PacketType.MsgTotemPole;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (TotemPoleAction)reader.ReadUInt16();
            reader.ReadUInt16();                        // 6 padding
            this.TotemType = reader.ReadUInt32();       // 8
            this.ItemID = reader.ReadUInt32();          // 12
            this.Enhancement = reader.ReadUInt32();     // 16
            this.Reserved = reader.ReadUInt32();        // 20
            this.EnhancementExpiration = reader.ReadUInt32(); // 24
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);          // 4
            writer.Write((ushort)0);                    // 6
            writer.Write(this.TotemType);               // 8
            writer.Write(this.ItemID);                  // 12
            writer.Write(this.Enhancement);             // 16
            writer.Write(this.Reserved);                // 20
            writer.Write(this.EnhancementExpiration);   // 24
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Guilds.HandleTotemPoleAsync(client, this);
    }
}

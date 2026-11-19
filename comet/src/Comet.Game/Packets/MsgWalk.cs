namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    public sealed class MsgWalk : MsgBase<Client>
    {
        public uint Direction { get; set; }
        public uint CharacterID { get; set; }
        public uint Mode { get; set; }
        public uint Timestamp { get; set; }
        public uint Map { get; set; }

        // The client sends the direction as any number whose remainder by 8 is the direction.
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Direction = reader.ReadUInt32();
            this.CharacterID = reader.ReadUInt32();
            this.Mode = reader.ReadUInt32();
            this.Timestamp = reader.ReadUInt32();
            this.Map = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgWalk);
            writer.Write(this.Direction);
            writer.Write(this.CharacterID);
            writer.Write(this.Mode);
            writer.Write(this.Timestamp);
            writer.Write(this.Map);
            return writer.ToArray();
        }

        public byte Facing => (byte)(this.Direction % 8);

        public override Task ProcessAsync(Client client) => Movement.WalkAsync(client, this);
    }
}

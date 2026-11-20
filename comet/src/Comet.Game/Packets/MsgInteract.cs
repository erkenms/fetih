namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Combat;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 5517 layout (Albetros), 40 bytes:
    // 4 timestamp, 8 sender, 12 target, 16 x, 18 y, 20 action u32, 24 data (damage; for a kill the
    // kill count), 28 unknown, 32 activation type (crit, block, ...), 36 activation value.
    // Magic attacks (24) are obfuscated, but magic isn't handled yet.
    public sealed class MsgInteract : MsgBase<Client>
    {
        public uint Timestamp { get; set; }
        public uint SenderID { get; set; }
        public uint TargetID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public InteractionType Action { get; set; }
        public uint Data { get; set; }

        public MsgInteract() => base.Type = PacketType.MsgInteract;

        public MsgInteract(InteractionType action, uint sender, uint target, ushort x, ushort y, uint data) : this()
        {
            this.Timestamp = (uint)Environment.TickCount;
            this.Action = action;
            this.SenderID = sender;
            this.TargetID = target;
            this.X = x;
            this.Y = y;
            this.Data = data;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Timestamp = reader.ReadUInt32();
            this.SenderID = reader.ReadUInt32();
            this.TargetID = reader.ReadUInt32();
            this.X = reader.ReadUInt16();
            this.Y = reader.ReadUInt16();
            this.Action = (InteractionType)reader.ReadUInt32();
            this.Data = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Timestamp);
            writer.Write(this.SenderID);
            writer.Write(this.TargetID);
            writer.Write(this.X);
            writer.Write(this.Y);
            writer.Write((uint)this.Action);
            writer.Write(this.Data);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Attacks.RequestAsync(client, this);

        public enum InteractionType : uint
        {
            Attack = 2,
            Kill = 14,
            MagicAttack = 24,
            Shoot = 28,
        }
    }
}

namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Combat;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 40 bytes:
    // 4 timestamp, 8 sender, 12 target, 16 x, 18 y, 20 action u32, 24 data (damage; for a kill the
    // kill count), 28 unknown, 32 activation type (crit, block, ...), 36 activation value.
    // Magic attacks (24) arrive with target, x, y and the magic type obfuscated by the sender's ID.
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
            if (this.Action == InteractionType.MagicAttack)
                this.DecodeMagicAttack();
        }

        public ushort MagicType => (ushort)this.Data;

        private void DecodeMagicAttack()
        {
            uint user = this.SenderID;
            uint magicType = (ExchangeShortBits((this.Data & 0xFFFF) ^ user ^ 0x915D, 16 - 3) + 0x14BE) & 0xFFFF;
            this.TargetID = (ExchangeLongBits(this.TargetID, 13) ^ user ^ 0x5F2D2463) + 0x8B90B51A;
            this.X = (ushort)(ExchangeShortBits(this.X ^ user ^ 0x2ED6, 16 - 1) + 0xDD12);
            this.Y = (ushort)(ExchangeShortBits(this.Y ^ user ^ 0xB99B, 16 - 5) + 0x76DE);
            this.Data = (this.Data & 0xFFFF0000) | magicType;
        }

        private static uint ExchangeShortBits(uint data, int bits)
        {
            data &= 0xFFFF;
            return ((data >> bits) | (data << (16 - bits))) & 0xFFFF;
        }

        private static uint ExchangeLongBits(uint data, int bits) => (data >> bits) | (data << (32 - bits));

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

        public override Task ProcessAsync(Client client) =>
            this.Action == InteractionType.MagicAttack
                ? Magic.Casting.CastAsync(client, this.MagicType, this.TargetID, this.X, this.Y)
                : Attacks.RequestAsync(client, this);

        public enum InteractionType : uint
        {
            Attack = 2,
            Kill = 14,
            MagicAttack = 24,
            Dash = 27,
            Shoot = 28,
            CounterKill = 43,
            CounterKillSwitch = 44,
        }
    }
}

namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A spell's effect as others see it. 5517 layout (Albetros):
    // 4 caster, 8 x, 10 y, 12 magic type u16, 14 magic level u16, 16 target count, then 32 bytes
    // per target: 0 ID, 4 damage, 8 hit (1), 12 activation type, 16 activation value; and
    // 32 bytes of zeros at the end.
    public sealed class MsgMagicEffect : MsgBase<Client>
    {
        // The Revive spell's effect: the client also leaves its dead state on it
        public const ushort Revive = 1050;

        public uint CasterID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort MagicType { get; set; }
        public ushort MagicLevel { get; set; }
        public List<(uint ID, uint Damage)> Targets { get; } = new List<(uint, uint)>();

        public MsgMagicEffect(uint caster, ushort x, ushort y, ushort magicType)
        {
            base.Type = PacketType.MsgMagicEffect;
            this.CasterID = caster;
            this.X = x;
            this.Y = y;
            this.MagicType = magicType;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.CasterID);
            writer.Write(this.X);
            writer.Write(this.Y);
            writer.Write(this.MagicType);
            writer.Write(this.MagicLevel);
            writer.Write((uint)this.Targets.Count);
            foreach (var (id, damage) in this.Targets)
            {
                writer.Write(id);
                writer.Write(damage);
                writer.Write(1u);
                writer.Write(new byte[20]);
            }
            writer.Write(new byte[32]);
            return writer.ToArray();
        }
    }
}

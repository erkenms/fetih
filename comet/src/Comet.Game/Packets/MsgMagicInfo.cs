namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A learned spell, sent on login and when it changes. 20 bytes:
    // 4 experience, 8 magic type u16, 10 level u16, 8 bytes of zeros.
    public sealed class MsgMagicInfo : MsgBase<Client>
    {
        public uint Experience { get; set; }
        public ushort MagicType { get; set; }
        public ushort Level { get; set; }

        public MsgMagicInfo(ushort magicType, ushort level, uint experience)
        {
            base.Type = PacketType.MsgMagicInfo;
            this.MagicType = magicType;
            this.Level = level;
            this.Experience = experience;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Experience);
            writer.Write(this.MagicType);
            writer.Write(this.Level);
            writer.Write(0UL);
            return writer.ToArray();
        }
    }
}

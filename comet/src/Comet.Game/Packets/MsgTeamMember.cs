namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Action byte at 4 (0 add), count at 5, then from 8 per member the name (16 bytes), ID,
    // look, max health and health (ushorts). Always 152 bytes, with room for one member.
    public sealed class MsgTeamMember : MsgBase<Client>
    {
        public const int Size = 152;

        public string Name { get; set; }
        public uint ID { get; set; }
        public uint Look { get; set; }
        public ushort MaxHealth { get; set; }
        public ushort Health { get; set; }

        public MsgTeamMember() => base.Type = PacketType.MsgTeamMember;

        public MsgTeamMember(Character member, ushort maxHealth) : this()
        {
            this.Name = member.Name;
            this.ID = member.CharacterID;
            this.Look = member.Mesh + member.Avatar * 10000u;
            this.MaxHealth = maxHealth;
            this.Health = member.HealthPoints;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((byte)0);                  // 4 add
            writer.Write((byte)1);                  // 5 count
            writer.Write((ushort)0);
            writer.Write(this.Name, 16);            // 8
            writer.Write(this.ID);                  // 24
            writer.Write(this.Look);                // 28
            writer.Write(this.MaxHealth);           // 32
            writer.Write(this.Health);              // 34
            writer.Write(new byte[Size - 36]);
            return writer.ToArray();
        }
    }
}

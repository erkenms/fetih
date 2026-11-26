namespace Fetih.Game.Packets
{
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The match score: damage each fighter dealt. 56 bytes: first
    // fighter's ID at 4, name (16 bytes) at 8, damage at 24, second fighter's ID at 28, name
    // at 32, damage at 48. All zero clears the score window.
    public sealed class MsgArenicScore : MsgBase<Client>
    {
        public const int Size = 56;

        public uint IdA { get; set; }
        public string NameA { get; set; }
        public uint DamageA { get; set; }
        public uint IdB { get; set; }
        public string NameB { get; set; }
        public uint DamageB { get; set; }

        public MsgArenicScore() => base.Type = PacketType.MsgArenicScore;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.IdA);                         // 4
            writer.Write(this.NameA ?? string.Empty, 16);   // 8
            writer.Write(this.DamageA);                     // 24
            writer.Write(this.IdB);                         // 28
            writer.Write(this.NameB ?? string.Empty, 16);   // 32
            writer.Write(this.DamageB);                     // 48
            writer.Write(0u);                               // 52
            return writer.ToArray();
        }
    }
}

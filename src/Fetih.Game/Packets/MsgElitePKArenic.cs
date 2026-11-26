namespace Fetih.Game.Packets
{
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // An Elite PK match for one fighter (CMsgElitePKArenic). Action (u16) at 4: 2 the match
    // starts (opens the score window, its length in seconds as u16 at 44), 3 the result (the
    // victory effect if the u32 at 8 is 1, else the failure one), 4 out of the match (closes
    // the score window), 1 closes the tournament windows. The client reads nothing else.
    public sealed class MsgElitePKArenic : MsgBase<Client>
    {
        public const int Size = 48;

        public enum ArenicAction : ushort { Close = 1, Start = 2, Result = 3, Leave = 4 }

        public ArenicAction Action { get; set; }
        public bool Won { get; set; }
        public ushort Seconds { get; set; }

        public MsgElitePKArenic() => base.Type = PacketType.MsgElitePKArenic;

        public MsgElitePKArenic(ArenicAction action) : this() => this.Action = action;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);         // 4
            writer.Write((ushort)0);                   // 6
            writer.Write(this.Won ? 1u : 0u);          // 8
            writer.Write(new byte[32]);                // 12
            writer.Write(this.Seconds);                // 44
            writer.Write((ushort)0);                   // 46
            return writer.ToArray();
        }
    }
}

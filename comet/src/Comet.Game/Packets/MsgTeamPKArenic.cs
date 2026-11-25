namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A Team PK match for one fighter (CMsgTeamPKArenic 2230; the Skill Team PK's
    // CMsgTeamPopPKArenic 2250 is read the same way). Action (u16) at 4: 2 the match starts
    // (opens the score window, its length in seconds as u16 at 44), 3 the result (the victory
    // effect if the u32 at 8 is 1, else the failure one), 4 out of the match (closes the score
    // window), 5 back in a fight, 6 out of the fight if the u32 at 12 is the hero, 7 closes the
    // tournament windows, 8 the "received EXP and Honor Points" message box.
    public sealed class MsgTeamPKArenic : MsgBase<Client>
    {
        public const int Size = 48;

        public enum ArenicAction : ushort { Start = 2, Result = 3, Leave = 4, Rejoin = 5, Out = 6, Close = 7, Awarded = 8 }

        public ArenicAction Action { get; set; }
        public bool Won { get; set; }
        public uint PlayerID { get; set; }
        public ushort Seconds { get; set; }

        public MsgTeamPKArenic(PacketType type, ArenicAction action)
        {
            base.Type = type;
            this.Action = action;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);         // 4
            writer.Write((ushort)0);                   // 6
            writer.Write(this.Won ? 1u : 0u);          // 8
            writer.Write(this.PlayerID);               // 12
            writer.Write(new byte[28]);                // 16
            writer.Write(this.Seconds);                // 44
            writer.Write((ushort)0);                   // 46
            return writer.ToArray();
        }
    }
}

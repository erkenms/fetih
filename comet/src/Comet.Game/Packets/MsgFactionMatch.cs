namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The Guild PK Tournament's match window (CMsgFactionMatch, CDlgFactionMatchStatus), shown
    // on maps flagged FactionPk. Action u16 at 4: 1 closes it; 2 fills and opens it with the
    // two guilds' names (36 bytes at 6 and 42), each one's guild points and members present
    // (u32 at 80 and 84, then 88 and 92), seconds left (u32 at 96), u32 at 100 (0 here) and the
    // player's Exploits (u32 at 104, 0xFFFFFFFF leaves them as they were).
    public sealed class MsgFactionMatch : MsgBase<Client>
    {
        public const int Size = 108;

        public enum MatchAction : ushort { Close = 1, Status = 2 }

        public MatchAction Action { get; set; }
        public string NameA { get; set; }
        public string NameB { get; set; }
        public uint PointsA { get; set; }
        public uint MembersA { get; set; }
        public uint PointsB { get; set; }
        public uint MembersB { get; set; }
        public uint SecondsLeft { get; set; }
        public uint Exploits { get; set; } = uint.MaxValue;

        public MsgFactionMatch() => base.Type = PacketType.MsgFactionMatch;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);              // 4
            writer.Write(this.NameA ?? string.Empty, 36);   // 6
            writer.Write(this.NameB ?? string.Empty, 36);   // 42
            writer.Write((ushort)0);                        // 78
            writer.Write(this.PointsA);                     // 80
            writer.Write(this.MembersA);                    // 84
            writer.Write(this.PointsB);                     // 88
            writer.Write(this.MembersB);                    // 92
            writer.Write(this.SecondsLeft);                 // 96
            writer.Write(0u);                               // 100
            writer.Write(this.Exploits);                    // 104
            return writer.ToArray();
        }
    }
}

namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A Team PK match's score (CMsgTeamPKArenicScore 2231, the Skill Team PK's 2251): two
    // 60-byte TEAM_PK_ARENIC_SCORE_INFO at 4 and 64. Each: ID at 0 (shown as the side's
    // fighter), team ID at 4 (the window marks the side whose ID is the hero's team, the
    // leader's ID, as "my team"), name from 24, score at 56.
    public sealed class MsgTeamPKArenicScore : MsgBase<Client>
    {
        public const int Size = 124;

        public uint TeamA { get; set; }
        public string NameA { get; set; }
        public uint ScoreA { get; set; }
        public uint TeamB { get; set; }
        public string NameB { get; set; }
        public uint ScoreB { get; set; }

        public MsgTeamPKArenicScore(PacketType type) => base.Type = type;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            Side(writer, this.TeamA, this.NameA, this.ScoreA);     // 4
            Side(writer, this.TeamB, this.NameB, this.ScoreB);     // 64
            return writer.ToArray();
        }

        private static void Side(PacketWriter writer, uint team, string name, uint score)
        {
            writer.Write(team);                         // 0
            writer.Write(team);                         // 4
            writer.Write(new byte[16]);                 // 8
            writer.Write(name ?? string.Empty, 32);     // 24
            writer.Write(score);                        // 56
        }
    }
}

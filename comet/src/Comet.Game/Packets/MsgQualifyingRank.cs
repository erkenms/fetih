namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Arena;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The arena ranking pages. The 5619 client asks with 52 bytes: kind u16 at 4 (0 arena
    // points, 1 honor), page u16 at 6 (from 1). Answered with kind at 4, page at 6, players
    // ranked at 8, players on this page at 12, then 36 bytes per player from 16: rank u16, name (16
    // bytes), kind u16, points (or honor), class, level, 0. Always room for one player.
    public sealed class MsgQualifyingRank : MsgBase<Client>
    {
        public const int PageSize = 10;
        private const int EntrySize = 36;

        public RankKind Kind { get; set; }
        public ushort Page { get; set; }
        public int Total { get; set; }
        public List<(uint Rank, Fighter Fighter)> Shown { get; } = new List<(uint, Fighter)>();

        public MsgQualifyingRank() => base.Type = PacketType.MsgQualifyingRank;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Kind = (RankKind)reader.ReadUInt16();
            this.Page = reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Kind);        // 4
            writer.Write(this.Page);                // 6
            writer.Write((uint)this.Total);         // 8
            writer.Write((uint)this.Shown.Count);   // 12
            foreach (var (rank, fighter) in this.Shown)
            {
                writer.Write((ushort)rank);
                writer.Write(fighter.Name ?? string.Empty, 16);
                writer.Write((ushort)this.Kind);
                writer.Write(this.Kind == RankKind.Honor ? fighter.HonorTotal : fighter.Points);
                writer.Write(fighter.Class);
                writer.Write(fighter.Level);
                writer.Write(0u);
            }
            if (this.Shown.Count == 0)
                writer.Write(new byte[EntrySize]);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Qualifier.SendRankingAsync(client, this.Kind, this.Page);

        public enum RankKind : ushort
        {
            Points = 0,
            Honor = 1,
        }
    }
}

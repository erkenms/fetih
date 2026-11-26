namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Arena;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Matches being fought. The 5619 client asks with 148 bytes (page 1 at 4, 6 at 8, room
    // for one match). Answered with the page at 4, 6 per page at 8, matches at 12, players
    // waiting at 16, 0 at 20, matches in this packet at 24, then 120 bytes per match from 28,
    // 60 per player: ID, look, name (16 bytes), level, class, 0, rank, arena points,
    // wins today, losses today, current honor, total honor. Always room for one match.
    public sealed class MsgQualifyingFightersList : MsgBase<Client>
    {
        public const uint PageSize = 6;
        private const int PlayerSize = 60;

        public uint Page { get; set; }
        public int Matches { get; set; }
        public int Waiting { get; set; }
        public List<(Fighter A, Fighter B)> Shown { get; } = new List<(Fighter, Fighter)>();

        public MsgQualifyingFightersList() => base.Type = PacketType.MsgQualifyingFightersList;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Page = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Page);                // 4
            writer.Write(PageSize);                 // 8
            writer.Write((uint)this.Matches);       // 12
            writer.Write((uint)this.Waiting);       // 16
            writer.Write(0u);                       // 20
            writer.Write((uint)this.Shown.Count);   // 24
            foreach (var (a, b) in this.Shown)      // 28
            {
                WritePlayer(writer, a);
                WritePlayer(writer, b);
            }
            if (this.Shown.Count == 0)
                writer.Write(new byte[2 * PlayerSize]);
            return writer.ToArray();
        }

        private static void WritePlayer(PacketWriter writer, Fighter fighter)
        {
            writer.Write(fighter.ID);
            writer.Write(fighter.Mesh);
            writer.Write(fighter.Name ?? string.Empty, 16);
            writer.Write(fighter.Level);
            writer.Write(fighter.Class);
            writer.Write(0u);
            writer.Write(fighter.Rank);
            writer.Write(fighter.Points);
            writer.Write(fighter.WinsToday);
            writer.Write(fighter.LossesToday);
            writer.Write(fighter.HonorPoints);
            writer.Write(fighter.HonorTotal);
        }

        public override Task ProcessAsync(Client client) => Qualifier.SendFightersAsync(client, this.Page);
    }
}

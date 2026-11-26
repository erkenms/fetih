namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Arena;
    using Fetih.Game.Database.Models;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Last season's top ten. The 5619 client asks with 60 bytes: count at 4, then room for
    // one 52-byte player (ID, name (16 bytes), look, level, class, 0,
    // rank, points, wins, losses). Always room for one player.
    public sealed class MsgQualifyingSeasonRankList : MsgBase<Client>
    {
        private const int EntrySize = 52;

        public List<DbArenaSeason> Entries { get; } = new List<DbArenaSeason>();

        public MsgQualifyingSeasonRankList() => base.Type = PacketType.MsgQualifyingSeasonRankList;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Entries.Count);         // 4
            foreach (var entry in this.Entries)             // 8
            {
                writer.Write(entry.CharacterID);
                writer.Write(entry.Name ?? string.Empty, 16);
                writer.Write(entry.Mesh);
                writer.Write((uint)entry.Level);
                writer.Write((uint)entry.Class);
                writer.Write(0u);
                writer.Write(entry.Rank);
                writer.Write(entry.Points);
                writer.Write(entry.Wins);
                writer.Write(entry.Losses);
            }
            if (this.Entries.Count == 0)
                writer.Write(new byte[EntrySize]);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Qualifier.SendSeasonRankAsync(client);
    }
}

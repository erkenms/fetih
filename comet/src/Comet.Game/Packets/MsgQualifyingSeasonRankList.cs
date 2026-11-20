namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Arena;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Last season's top ten. The 5619 client asks with 60 bytes: count at 4, then room for
    // one 52-byte player (Albetros 5517 / 5808: ID, name (16 bytes), look, level, class, 0,
    // rank, points, wins, losses). There are no seasons yet, so the list is always empty.
    public sealed class MsgQualifyingSeasonRankList : MsgBase<Client>
    {
        private const int EntrySize = 52;

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
            writer.Write(0u);                   // 4
            writer.Write(new byte[EntrySize]);  // 8
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => client.SendAsync(new MsgQualifyingSeasonRankList());
    }
}

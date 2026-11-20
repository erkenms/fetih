namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 5517 layout (Albetros): the client asks with the index of the first member it wants
    // at 8; the reply has the count at 12 and 48-byte entries from 16: name (16 bytes),
    // level at 24, rank at 28, total donation at 36, online at 40
    public sealed class MsgSynMemberList : MsgBase<Client>
    {
        public const int EntrySize = 48;
        public const int PageSize = 12;

        public uint StartIndex { get; set; }
        public List<(GuildMember Member, bool Online)> Members { get; set; } =
            new List<(GuildMember, bool)>();

        public MsgSynMemberList() => base.Type = PacketType.MsgSynMemberList;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            reader.ReadUInt32();
            this.StartIndex = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(0u);                           // 4
            writer.Write(this.StartIndex);              // 8
            writer.Write(this.Members.Count);           // 12
            foreach (var (member, online) in this.Members)
            {
                writer.Write(member.Name, 16);          // +0
                writer.Write(0u);                       // +16
                writer.Write(0u);                       // +20
                writer.Write((uint)member.Level);       // +24
                writer.Write((uint)member.Rank);        // +28
                writer.Write(0u);                       // +32
                writer.Write(member.TotalDonation);     // +36
                writer.Write(online ? 1u : 0u);         // +40
                writer.Write(0u);                       // +44
            }
            writer.Write(0u);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Guilds.SendMembersAsync(client, this.StartIndex);
    }
}

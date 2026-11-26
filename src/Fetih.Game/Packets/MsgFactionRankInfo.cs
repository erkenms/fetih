namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Flowers;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Guild donation ranking (2101). The client asks with an 80-byte packet carrying the rank
    // type (0..9) at 4 and 0-based page index at 5. The reply is 12 + count * 68 bytes: rank
    // type at 4, page at 5, count at 6, total ranked members (u16) at 8, then 68-byte entries
    // from 12: member ID (+0), rank (+4), 0-based position (+8), silver (+12), CPs (+16),
    // guide (+20), PK (+24), arsenal (+28), rose (+32), orchid (+36), lily (+40), tulip (+44),
    // total (+48), and 16-byte name (+52).
    public sealed class MsgFactionRankInfo : MsgBase<Client>
    {
        public const int HeaderSize = 12;
        public const int EntrySize = 68;
        public const int PageSize = 10;
        public const int MaxRankTypes = 10;

        public enum FactionRankType : byte
        {
            Silver = 0,
            ConquerPoints = 1,
            Guide = 2,
            Pk = 3,
            Arsenal = 4,
            Rose = 5,
            Orchid = 6,
            Lily = 7,
            Tulip = 8,
            Total = 9,
        }

        public FactionRankType RankType { get; set; }
        public byte PageIndex { get; set; }
        public ushort MaxRankSize { get; set; }
        public List<(GuildMember Member, uint Position)> Entries { get; set; } =
            new List<(GuildMember, uint)>();
        public Dictionary<uint, FlowerRecord> Flowers { get; set; } = new Dictionary<uint, FlowerRecord>();

        public MsgFactionRankInfo() => base.Type = PacketType.MsgFactionRankInfo;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.RankType = (FactionRankType)reader.ReadByte();
            this.PageIndex = reader.ReadByte();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((byte)this.RankType);          // 4
            writer.Write(this.PageIndex);               // 5
            writer.Write((byte)this.Entries.Count);     // 6
            writer.Write((byte)0);                      // 7
            writer.Write(this.MaxRankSize);             // 8
            writer.Write((ushort)0);                    // 10
            foreach (var (member, position) in this.Entries)
            {
                writer.Write(member.CharacterID);       // +0
                writer.Write((uint)member.Rank);        // +4
                writer.Write(position);                 // +8
                writer.Write(member.SilverDonation);    // +12
                writer.Write(member.ConquerPointsDonation); // +16
                writer.Write(0u);                       // +20 guide
                writer.Write(0u);                       // +24 PK
                writer.Write(member.ArsenalDonation);   // +28 arsenal
                this.Flowers.TryGetValue(member.CharacterID, out var flowers);
                writer.Write(flowers?.RedRoses ?? 0);   // +32 rose
                writer.Write(flowers?.Orchids ?? 0);    // +36 orchid
                writer.Write(flowers?.Lilies ?? 0);     // +40 lily
                writer.Write(flowers?.Tulips ?? 0);     // +44 tulip
                writer.Write(member.TotalDonation);     // +48 total
                writer.Write(member.Name, 16);          // +52
            }
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) =>
            Guilds.SendDonationRankingAsync(client, this.RankType, this.PageIndex);
    }
}

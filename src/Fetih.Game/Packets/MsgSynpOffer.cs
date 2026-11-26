namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The donation tab, 52 bytes: which donations follow (flags) at 4, then silver, CPs,
    // guide, PK, arsenal, rose, orchid, lily, tulip and exploits as u32 from 8. The client
    // asks with the same type.
    public sealed class MsgSynpOffer : MsgBase<Client>
    {
        public const uint AllDonations = 0x1F;

        public uint Silver { get; set; }
        public uint ConquerPoints { get; set; }
        public uint Arsenal { get; set; }

        public MsgSynpOffer() => base.Type = PacketType.MsgSynpOffer;

        public MsgSynpOffer(GuildMember member) : this()
        {
            this.Silver = member?.SilverDonation ?? 0;
            this.ConquerPoints = member?.ConquerPointsDonation ?? 0;
            this.Arsenal = member?.ArsenalDonation ?? 0;
        }

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
            writer.Write(AllDonations);         // 4
            writer.Write(this.Silver);          // 8
            writer.Write(this.ConquerPoints);   // 12
            writer.Write(0u);                   // 16 guide
            writer.Write(0u);                   // 20 PK
            writer.Write(this.Arsenal);         // 24 arsenal
            writer.Write(new byte[28]);         // 28
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Guilds.SendDonationsAsync(client);
    }
}

namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The donation tab, 52 bytes: which donations follow (flags) at 4, then silver, CPs,
    // guide, PK, arsenal, rose, orchid, lily, tulip and exploits as u32 from 8. The client
    // asks with the same type.
    public sealed class MsgSynpOffer : MsgBase<Client>
    {
        public const uint AllDonations = 0x1F;

        public uint Silver { get; set; }
        public uint ConquerPoints { get; set; }

        public MsgSynpOffer() => base.Type = PacketType.MsgSynpOffer;

        public MsgSynpOffer(GuildMember member) : this()
        {
            this.Silver = member?.SilverDonation ?? 0;
            this.ConquerPoints = member?.ConquerPointsDonation ?? 0;
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
            writer.Write(new byte[40]);         // 16
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Guilds.SendDonationsAsync(client);
    }
}

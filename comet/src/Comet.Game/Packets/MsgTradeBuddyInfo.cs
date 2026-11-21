namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A partner's ID at 4, mesh at 8, level byte at 12, class byte at 13, PK
    // points at 14, guild ID at 16 and rank at 20, the spouse's name (16 bytes) at 26
    public sealed class MsgTradeBuddyInfo : MsgBase<Client>
    {
        public uint ID { get; set; }
        public uint Mesh { get; set; }
        public byte Level { get; set; }
        public byte Class { get; set; }
        public ushort PkPoints { get; set; }
        public uint GuildID { get; set; }
        public uint GuildRank { get; set; }
        public string Spouse { get; set; } = "None";

        public MsgTradeBuddyInfo() => base.Type = PacketType.MsgTradeBuddyInfo;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.ID);                  // 4
            writer.Write(this.Mesh);                // 8
            writer.Write(this.Level);               // 12
            writer.Write(this.Class);               // 13
            writer.Write(this.PkPoints);            // 14
            writer.Write(this.GuildID);             // 16
            writer.Write(this.GuildRank);           // 20
            writer.Write((ushort)0);                // 24
            writer.Write(this.Spouse, 16);          // 26
            return writer.ToArray();
        }
    }
}

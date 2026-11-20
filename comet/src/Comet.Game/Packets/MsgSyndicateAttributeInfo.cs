namespace Comet.Game.Packets
{
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The player's guild window, 92 bytes: guild ID at 4, the player's silver donation at 8,
    // fund (u64) at 12, CP fund at 20, members at 24, the player's rank at 28, leader name
    // (16 bytes) at 32, join requirements at 48, 52 and 56, guild level (byte) at 60, the
    // player's join date (YYYYMMDD) at 67. All zero for a player without a guild.
    public sealed class MsgSyndicateAttributeInfo : MsgBase<Client>
    {
        public const int Size = 92;

        private readonly Guild Guild;
        private readonly GuildMember Member;

        public MsgSyndicateAttributeInfo(Guild guild, GuildMember member)
        {
            base.Type = PacketType.MsgSyndicateAttributeInfo;
            this.Guild = guild;
            this.Member = member;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            var guild = this.Member == null ? null : this.Guild;
            writer.Write(guild?.ID ?? 0);                           // 4
            writer.Write(this.Member?.SilverDonation ?? 0);         // 8
            writer.Write(guild?.Fund ?? 0);                         // 12
            writer.Write(guild?.ConquerPointsFund ?? 0);            // 20
            writer.Write(guild?.Members.Count ?? 0);                // 24
            writer.Write((uint)(this.Member?.Rank ?? GuildRank.None)); // 28
            writer.Write(guild?.Leader?.Name ?? string.Empty, 16);  // 32
            writer.Write(guild?.RequiredLevel ?? 0);                // 48
            writer.Write(guild?.RequiredRebirths ?? 0);             // 52
            writer.Write(guild?.RequiredProfessions ?? 0);          // 56
            writer.Write(guild?.Level ?? 0);                        // 60
            writer.Write(new byte[6]);                              // 61
            writer.Write(this.Member?.Joined ?? 0);                 // 67
            writer.Write(new byte[Size - 71]);                      // 71
            return writer.ToArray();
        }
    }
}

namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Xunit;

    public class GuildPacketsTests
    {
        private static Guild NewGuild()
        {
            var guild = new Guild(7, "Lions") { LeaderID = 1000001, Fund = 500_000, ConquerPointsFund = 30, RequiredLevel = 15 };
            guild.Members[1000001] = new GuildMember(1000001, "Altay", 120, GuildRank.Leader, 20261120);
            guild.Members[1000002] = new GuildMember(1000002, "Berk", 40, GuildRank.Member, 20261121) { SilverDonation = 900 };
            return guild;
        }

        [Fact]
        public void SyndicateHasActionDataRequirementsThenStrings()
        {
            var bytes = new MsgSyndicate(MsgSyndicate.SyndicateAction.AllyRequest, 7, "Lions").Encode();
            Assert.Equal(1107, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(23u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal((1, 5), (bytes[24], bytes[25]));
            Assert.Equal("Lions", Encoding.ASCII.GetString(bytes, 26, 5));

            var msg = new MsgSyndicate();
            msg.Decode(Comet.Client.SyndicateMessage.Build(Comet.Client.SyndicateMessage.AllyRequest, 7, "Lions"));
            Assert.Equal((MsgSyndicate.SyndicateAction.AllyRequest, 7u, "Lions"), (msg.Action, msg.Data, msg.Text));
        }

        [Fact]
        public void AttributeInfoCarriesTheGuildAndTheMember()
        {
            var guild = NewGuild();
            var bytes = new MsgSyndicateAttributeInfo(guild, guild.Members[1000002]).Encode();
            Assert.Equal(MsgSyndicateAttributeInfo.Size, bytes.Length);
            Assert.Equal(1106, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(900u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(500_000ul, BitConverter.ToUInt64(bytes, 12));
            Assert.Equal(30u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 24));
            Assert.Equal(200u, BitConverter.ToUInt32(bytes, 28));
            Assert.Equal("Altay", Encoding.ASCII.GetString(bytes, 32, 5));
            Assert.Equal(15u, BitConverter.ToUInt32(bytes, 48));
            Assert.Equal(1, bytes[60]);
            Assert.Equal(20261121u, BitConverter.ToUInt32(bytes, 67));

            var none = new MsgSyndicateAttributeInfo(null, null).Encode();
            Assert.Equal(MsgSyndicateAttributeInfo.Size, none.Length);
            Assert.Equal(0u, BitConverter.ToUInt32(none, 4));
        }

        [Fact]
        public void MemberListHas48ByteEntries()
        {
            var guild = NewGuild();
            var list = new MsgSynMemberList { StartIndex = 0 };
            list.Members.Add((guild.Members[1000001], true));
            list.Members.Add((guild.Members[1000002], false));
            var bytes = list.Encode();
            Assert.Equal(16 + 2 * MsgSynMemberList.EntrySize + 4, bytes.Length);
            Assert.Equal(2102, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2, BitConverter.ToInt32(bytes, 12));
            int second = 16 + MsgSynMemberList.EntrySize;
            Assert.Equal("Berk", Encoding.ASCII.GetString(bytes, second, 4));
            Assert.Equal(40u, BitConverter.ToUInt32(bytes, second + 24));
            Assert.Equal(200u, BitConverter.ToUInt32(bytes, second + 28));
            Assert.Equal(900u, BitConverter.ToUInt32(bytes, second + 36));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, second + 40));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 16 + 40));
        }

        [Fact]
        public void FactionRankInfoDecodesQueryAndEncodes68ByteEntries()
        {
            // Real 80-byte query from the 5619 client (rank type 4, page 0)
            var query = new byte[80];
            BitConverter.TryWriteBytes(query.AsSpan(0), (ushort)80);
            BitConverter.TryWriteBytes(query.AsSpan(2), (ushort)2101);
            query[4] = (byte)MsgFactionRankInfo.FactionRankType.Arsenal;
            query[5] = 2;

            var decoded = new MsgFactionRankInfo();
            decoded.Decode(query);
            Assert.Equal(MsgFactionRankInfo.FactionRankType.Arsenal, decoded.RankType);
            Assert.Equal((byte)2, decoded.PageIndex);

            var guild = NewGuild();
            guild.Members[1000001].SilverDonation = 1500;
            guild.Members[1000001].ConquerPointsDonation = 40;
            guild.Members[1000002].ConquerPointsDonation = 250;

            var reply = new MsgFactionRankInfo
            {
                RankType = MsgFactionRankInfo.FactionRankType.Silver,
                PageIndex = 0,
                MaxRankSize = 2,
            };
            reply.Entries.Add((guild.Members[1000001], 0));
            reply.Entries.Add((guild.Members[1000002], 1));

            var bytes = reply.Encode();
            Assert.Equal(MsgFactionRankInfo.HeaderSize + 2 * MsgFactionRankInfo.EntrySize, bytes.Length);
            Assert.Equal(2101, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((byte)MsgFactionRankInfo.FactionRankType.Silver, bytes[4]);
            Assert.Equal((byte)0, bytes[5]);
            Assert.Equal((byte)2, bytes[6]);
            Assert.Equal((ushort)2, BitConverter.ToUInt16(bytes, 8));

            int first = MsgFactionRankInfo.HeaderSize;
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, first));
            Assert.Equal(1000u, BitConverter.ToUInt32(bytes, first + 4));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, first + 8));
            Assert.Equal(1500u, BitConverter.ToUInt32(bytes, first + 12));
            Assert.Equal(40u, BitConverter.ToUInt32(bytes, first + 16));
            for (int off = 20; off <= 44; off += 4)
                Assert.Equal(0u, BitConverter.ToUInt32(bytes, first + off));
            Assert.Equal(1540u, BitConverter.ToUInt32(bytes, first + 48));
            Assert.Equal("Altay", Encoding.ASCII.GetString(bytes, first + 52, 5));

            int second = first + MsgFactionRankInfo.EntrySize;
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, second));
            Assert.Equal(200u, BitConverter.ToUInt32(bytes, second + 4));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, second + 8));
            Assert.Equal(900u, BitConverter.ToUInt32(bytes, second + 12));
            Assert.Equal(250u, BitConverter.ToUInt32(bytes, second + 16));
            Assert.Equal(1150u, BitConverter.ToUInt32(bytes, second + 48));
            Assert.Equal("Berk", Encoding.ASCII.GetString(bytes, second + 52, 4));
        }

        [Fact]
        public void TheSummaryNamesTheLeaderLevelAndMembers()
        {
            Assert.Equal("Lions Altay 1 2", NewGuild().Summary);
        }

        [Theory]
        [InlineData("Lions", null)]
        [InlineData("L", "invalid_name")]
        [InlineData("Two Words", "invalid_name")]
        [InlineData("[GM]Guild", "invalid_name")]
        [InlineData("ThisNameIsTooLong", "invalid_name")]
        public void GuildNamesAreOneShortWordWithoutGmTags(string name, string reason)
        {
            Assert.Equal(reason, Guilds.CheckName(name));
        }
    }
}

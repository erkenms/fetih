namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Xunit;

    public class ClanPacketsTests
    {
        [Fact]
        public void FamilyHasActionDataThenStringsAt16()
        {
            var bytes = new MsgFamily(MsgFamily.FamilyAction.Recruit, 1000001, "Wolves", "Altay").Encode();
            Assert.Equal(1312, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(9u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal((2, 6), (bytes[16], bytes[17]));
            Assert.Equal("Wolves", Encoding.ASCII.GetString(bytes, 18, 6));

            var msg = new MsgFamily();
            msg.Decode(bytes);
            Assert.Equal((MsgFamily.FamilyAction.Recruit, 1000001u, "Wolves"), (msg.Action, msg.Data, msg.Text));
        }

        [Fact]
        public void MemberListHas36ByteEntries()
        {
            var member = new ClanMember(1000002, "Berk", 55, ClanRank.Member, 20261120)
            {
                Class = 15, Mesh = 21003, Donation = 700,
            };
            var bytes = new MsgFamily(MsgFamily.FamilyAction.QueryMembers, 3)
            {
                Members = new List<(ClanMember, bool)> { (member, true) },
            }.Encode();
            Assert.Equal(20 + MsgFamily.MemberSize, bytes.Length);
            Assert.Equal(1, BitConverter.ToInt32(bytes, 16));
            Assert.Equal("Berk", Encoding.ASCII.GetString(bytes, 20, 4));
            Assert.Equal(55u, BitConverter.ToUInt32(bytes, 36));
            Assert.Equal(10, BitConverter.ToUInt16(bytes, 40));
            Assert.Equal(1, BitConverter.ToUInt16(bytes, 42));
            Assert.Equal(15u, BitConverter.ToUInt32(bytes, 44));
            Assert.Equal(21003u, BitConverter.ToUInt32(bytes, 48));
            Assert.Equal(700u, BitConverter.ToUInt32(bytes, 52));
        }

        [Fact]
        public void RelationListHas56ByteEntries()
        {
            var clan = new Clan(4, "Wolves") { LeaderID = 1000001 };
            clan.Members[1000001] = new ClanMember(1000001, "Altay", 60, ClanRank.Leader, 20261120);
            var bytes = new MsgFamily(MsgFamily.FamilyAction.AllyList, 3)
            {
                Relations = new List<Clan> { clan },
            }.Encode();
            Assert.Equal(20 + MsgFamily.RelationSize, bytes.Length);
            Assert.Equal(4u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal("Wolves", Encoding.ASCII.GetString(bytes, 24, 6));
            Assert.Equal("Altay", Encoding.ASCII.GetString(bytes, 60, 5));
        }
    }
}

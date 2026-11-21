namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class SocialPacketsTests
    {
        [Fact]
        public void TradeHasDataThenAction()
        {
            var bytes = new MsgTrade(MsgTrade.TradeAction.ShowSilver, 300).Encode();
            Assert.Equal(12, bytes.Length);
            Assert.Equal(1056, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(300u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(8u, BitConverter.ToUInt32(bytes, 8));

            var msg = new MsgTrade();
            msg.Decode(Convert.FromHexString("0C0020044A420F0006000000"));
            Assert.Equal((MsgTrade.TradeAction.AddItem, 1000010u), (msg.Action, msg.Data));
        }

        [Fact]
        public void RelationCarriesTheAskersLevelAndBattlePower()
        {
            var asker = new Character(new DbCharacter(1, 1000001, "Altay") { Level = 42 });
            var bytes = new MsgRelation(asker, 1000002).Encode();
            Assert.Equal(MsgRelation.Size, bytes.Length);
            Assert.Equal(2071, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(42u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(42u, BitConverter.ToUInt32(bytes, 16));
        }

        [Fact]
        public void TeamHasActionThenData()
        {
            var bytes = new MsgTeam(MsgTeam.TeamAction.Invite, 1000001).Encode();
            Assert.Equal(12, bytes.Length);
            Assert.Equal(1023, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(4u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 8));
        }

        [Fact]
        public void TeamMemberCarriesNameIdLookAndHealth()
        {
            var character = new Character(new DbCharacter(1, 1000001, "Altay")
            {
                Mesh = 1003, Avatar = 2, HealthPoints = 50, Vitality = 12, Level = 1
            });
            var bytes = new MsgTeamMember(character, 400).Encode();
            Assert.Equal(MsgTeamMember.Size, bytes.Length);
            Assert.Equal(1026, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((0, 1), (bytes[4], bytes[5]));
            Assert.Equal("Altay", Encoding.ASCII.GetString(bytes, 8, 5));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 24));
            Assert.Equal(21003u, BitConverter.ToUInt32(bytes, 28));
            Assert.Equal(400, BitConverter.ToUInt16(bytes, 32));
            Assert.Equal(50, BitConverter.ToUInt16(bytes, 34));
        }

        [Fact]
        public void FriendHasIdActionOnlineAndName()
        {
            var bytes = new MsgFriend(MsgFriend.FriendAction.GetInfo, 1000002, "Bora", true).Encode();
            Assert.Equal(MsgFriend.Size, bytes.Length);
            Assert.Equal(1019, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal((15, 1), (bytes[8], bytes[9]));
            Assert.Equal("Bora", Encoding.ASCII.GetString(bytes, 20, 4));
            Assert.Equal(0, bytes[24]);

            var msg = new MsgFriend();
            msg.Decode(Comet.Client.FriendMessage.Build(Comet.Client.FriendMessage.Apply, 1000002, "Bora"));
            Assert.Equal((MsgFriend.FriendAction.Apply, 1000002u, "Bora"), (msg.Action, msg.ID, msg.Name));
        }
    }
}

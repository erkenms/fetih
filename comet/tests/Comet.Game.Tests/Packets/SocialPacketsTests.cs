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
    }
}

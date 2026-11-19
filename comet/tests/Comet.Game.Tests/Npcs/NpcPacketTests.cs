namespace Comet.Game.Tests.Npcs
{
    using System;
    using System.Linq;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using Xunit;
    using static Comet.Game.Packets.MsgTaskDialog;

    public class NpcPacketTests
    {
        [Fact]
        public void NpcInfoLayout()
        {
            var bytes = new MsgNpcInfo(new DbNpc { NpcID = 10050, X = 435, Y = 440, Lookface = 280, Role = 29 }).Encode();
            Assert.Equal(2030, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((10050u, 10050u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8)));
            Assert.Equal((435, 440, 280, 29), (BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 14),
                BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18)));
            Assert.Equal(0, bytes[22]);
            Assert.Equal(24, bytes.Length);
        }

        [Fact]
        public void DialogPartsCarryOptionActionAndText()
        {
            var bytes = new MsgTaskDialog(DialogAction.Option, 3, "Desert City").Encode();
            Assert.Equal(2032, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((3, 2), (bytes[10], bytes[11]));
            Assert.Equal((1, 11), (bytes[12], bytes[13]));
            Assert.Equal("Desert City", System.Text.Encoding.Latin1.GetString(bytes, 14, 11));

            var answer = new MsgTaskDialog();
            answer.Decode(Client.DialogMessage.BuildAnswer(6));
            Assert.Equal(((byte)6, DialogAction.Answer), (answer.Option, answer.Action));
        }

        [Fact]
        public void NpcTableFindsNpcsInView()
        {
            var table = new NpcTable(new[]
            {
                new DbNpc { NpcID = 1, MapID = 1002, X = 100, Y = 100 },
                new DbNpc { NpcID = 2, MapID = 1002, X = 200, Y = 200 },
                new DbNpc { NpcID = 3, MapID = 1006, X = 100, Y = 100 },
            });
            Assert.Equal(3, table.Count);
            Assert.Equal(new uint[] { 1 }, table.InView(1002, 110, 90).Select(n => n.NpcID));
            Assert.Equal(2u, table.Find(1002, 2).NpcID);
            Assert.Null(table.Find(1006, 2));
            Assert.Empty(table.InView(1010, 100, 100));
        }
    }
}

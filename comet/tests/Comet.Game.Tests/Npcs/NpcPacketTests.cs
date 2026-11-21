namespace Comet.Game.Tests.Npcs
{
    using System;
    using System.Linq;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using Xunit;
    using static Comet.Game.Packets.MsgTaskDialog;

    public class NpcPacketTests
    {
        [Fact]
        public void NpcInfoLayout()
        {
            var bytes = new MsgNpcInfo(new Npc { NpcID = 10050, X = 435, Y = 440, Lookface = 280, Role = 29 }).Encode();
            Assert.Equal(2030, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((10050u, 10050u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8)));
            Assert.Equal((435, 440, 280, 29), (BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 14),
                BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18)));
            Assert.Equal(0, bytes[22]);
            Assert.Equal(24, bytes.Length);
        }

        [Fact]
        public void ABoothCarriesItsOwnersName()
        {
            var bytes = new MsgNpcInfo(50, 121, 217, 406, 14, "Seller").Encode();
            Assert.Equal((406, 14), (BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18)));
            Assert.Equal((1, 6), (bytes[22], bytes[23]));
            Assert.Equal("Seller", System.Text.Encoding.Latin1.GetString(bytes, 24, 6));
            Assert.Equal(31, bytes.Length);

            var spawn = Client.NpcSpawn.Parse(bytes);
            Assert.Equal((50u, "Seller"), (spawn.Id, spawn.Name));
        }

        [Fact]
        public void BoothItemsCarryBoothPriceAndCurrency()
        {
            var item = new Comet.Game.Database.Models.DbItem
            {
                ItemID = 7, TypeID = 410301, Amount = 1000, AmountLimit = 1099, Gem1 = 1, Plus = 3, Color = 3,
            };
            var bytes = new MsgItemInfoEx(item, 50, 300, MsgItemInfoEx.ViewType.ConquerPoints).Encode();
            Assert.Equal((84, 1108), (bytes.Length, BitConverter.ToUInt16(bytes, 2)));
            var parsed = Client.BoothItem.Parse(bytes);
            Assert.Equal(new Client.BoothItem(7, 50, 300, 410301, 1000, 1099, 3), parsed);
            Assert.Equal((1, 3, 3), (bytes[32], bytes[41], bytes[56]));
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
        public void TheShippedNpcFileLoads()
        {
            var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json"));
            Assert.True(npcs.Count > 300);
            var conductress = npcs.Single(n => n.NpcID == 10050);
            Assert.Equal((280, 29, 1002u, 435, 440), (conductress.Lookface, conductress.Role, conductress.MapID,
                conductress.X, conductress.Y));
        }

        [Fact]
        public void AnNpcIDListedTwiceIsRefused()
        {
            var path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, @"// two of the same
{ ""npcs"": [
  { ""id"": 7, ""lookface"": 10, ""role"": 2, ""map"": 1002, ""x"": 1, ""y"": 1 },
  { ""id"": 7, ""lookface"": 20, ""role"": 2, ""map"": 1006, ""x"": 2, ""y"": 2 },
] }");
                var e = Assert.Throws<System.IO.InvalidDataException>(() => NpcTable.Load(path));
                Assert.Contains("7", e.Message);
            }
            finally { System.IO.File.Delete(path); }
        }

        [Fact]
        public void NpcTableFindsNpcsInView()
        {
            var table = new NpcTable(new[]
            {
                new Npc { NpcID = 1, MapID = 1002, X = 100, Y = 100 },
                new Npc { NpcID = 2, MapID = 1002, X = 200, Y = 200 },
                new Npc { NpcID = 3, MapID = 1006, X = 100, Y = 100 },
            });
            Assert.Equal(3, table.Count);
            Assert.Equal(new uint[] { 1 }, table.InView(1002, 110, 90).Select(n => n.NpcID));
            Assert.Equal(2u, table.Find(1002, 2).NpcID);
            Assert.Null(table.Find(1006, 2));
            Assert.Empty(table.InView(1010, 100, 100));
        }
    }
}

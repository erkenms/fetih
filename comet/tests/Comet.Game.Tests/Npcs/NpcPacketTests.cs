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
        public void TheShippedPortalAndLevelFilesLoad()
        {
            var portals = PortalTable.Load(DataFiles.PathOf("portals.json"));
            var table = new PortalTable(portals);
            Assert.True(table.TryGet(1002, 3, out var stable));
            Assert.Equal(new PortalTarget(1006, 37, 31), stable);

            var levels = Comet.Game.States.ExperienceTable.Load(DataFiles.PathOf("levels.json"));
            var experience = new Comet.Game.States.ExperienceTable(levels);
            Assert.Equal(120, experience.MaxLevel);
            Assert.True(experience.TryGetRequired(1, out var first));
            Assert.Equal(59UL, first);
        }

        [Fact]
        public void APortalListedTwiceIsRefused()
        {
            var path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, @"{ ""portals"": [
  { ""map"": 1002, ""portal"": 3, ""toMap"": 1006, ""toX"": 1, ""toY"": 1 },
  { ""map"": 1002, ""portal"": 3, ""toMap"": 1004, ""toX"": 2, ""toY"": 2 }
] }");
                var e = Assert.Throws<System.IO.InvalidDataException>(() => PortalTable.Load(path));
                Assert.Contains("1002/3", e.Message);
            }
            finally { System.IO.File.Delete(path); }
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

namespace Fetih.Game.Tests.Npcs
{
    using System;
    using System.Linq;
    using System.Text;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Items;
    using Fetih.Game.Npcs;
    using Fetih.Game.Packets;
    using Fetih.Game.World;
    using Xunit;

    public class ShopTests
    {
        [Fact]
        public void ShopDatSectionsBecomeShops()
        {
            var shops = ShopTable.Parse(Encoding.Latin1.GetString(Fetih.TestClient.ItemFiles.ShopDat(
                (5, new uint[] { 410301, 510301 }), (3, new uint[] { 1000000 }))));
            Assert.Equal(2, shops.Count);
            Assert.True(shops.Find(5).Sells(510301));
            Assert.False(shops.Find(5).Sells(1000000));
            Assert.Equal(Shop.Silver, shops.Find(3).MoneyType);
            Assert.Null(shops.Find(4));
        }

        [Fact]
        public void TheFirstSectionOfAShopCounts()
        {
            var text = "[Header]\r\nAmount=2\r\n\r\n[Shop0]\r\nID=7\r\nMoneyType=0\r\nItemAmount=1\r\nItem0=100\r\n\r\n" +
                "[Shop1]\r\nID=7\r\nName=Õâ\r\nMoneyType=0\r\nItemAmount=2\r\nItem0=200\r\nItem1=300\r\n";
            var shop = ShopTable.Parse(text).Find(7);
            Assert.Equal(new uint[] { 100 }, shop.Items.ToArray());
        }

        [Theory]
        [InlineData(300u, 0, 0, 100u)]
        [InlineData(300u, 1099, 1099, 100u)]
        [InlineData(300u, 550, 1100, 50u)]
        [InlineData(300u, 0, 1100, 0u)]
        public void ShopkeepersPayAThirdLessWear(uint price, int durability, int max, uint expected) =>
            Assert.Equal(expected, Shopping.SellPrice(price, (ushort)durability, (ushort)max));

        [Fact]
        public void PackageLayout()
        {
            var items = Enumerable.Range(0, 2).Select(i => new DbItem
            {
                ItemID = 7 + (uint)i, TypeID = 410301, Gem1 = 1, Gem2 = 2, Plus = 3, Bless = 4, Enchant = 5, Color = 6,
            }).ToList();
            var bytes = new MsgPackage(8, MsgPackage.PackageAction.Query) { Items = items }.Encode();
            Assert.Equal((1102, 24 + 2 * 52), (BitConverter.ToUInt16(bytes, 2), bytes.Length));
            Assert.Equal((8u, 0, 10, 60u, 2), (BitConverter.ToUInt32(bytes, 4), bytes[8], bytes[9],
                BitConverter.ToUInt32(bytes, 12), bytes[20]));
            var second = 24 + 52;
            Assert.Equal((8u, 410301u), (BitConverter.ToUInt32(bytes, second), BitConverter.ToUInt32(bytes, second + 4)));
            Assert.Equal((1, 2, 3, 4, 5, 6), (bytes[second + 9], bytes[second + 10], bytes[second + 17],
                bytes[second + 18], bytes[second + 20], bytes[second + 27]));

            var parsed = Fetih.TestClient.PackageMessage.Parse(bytes);
            Assert.Equal(new[] { (7u, 410301u), (8u, 410301u) }, parsed.Items);
            var request = new MsgPackage();
            request.Decode(Fetih.TestClient.PackageMessage.Build(8, Fetih.TestClient.PackageMessage.Withdraw, 9));
            Assert.Equal((8u, MsgPackage.PackageAction.Withdraw, 9u), (request.NpcID, request.Action, request.ItemID));
        }

        [Fact]
        public void ShopsAndWindowsOpenAtTheirCells()
        {
            var shop = MsgAction.OpenShop(1000001, 5, 452, 336).Encode();
            Assert.Equal((452, 336), (BitConverter.ToUInt16(shop, 24), BitConverter.ToUInt16(shop, 26)));
            var window = MsgAction.OpenWindow(1000001, MsgAction.Window.Warehouse, 410, 355).Encode();
            Assert.Equal((4u, (ushort)410, (ushort)355, (ushort)126), (BitConverter.ToUInt32(window, 8),
                BitConverter.ToUInt16(window, 12), BitConverter.ToUInt16(window, 14), BitConverter.ToUInt16(window, 20)));
        }

        [Fact]
        public void TheShippedTeleporterFileLoads()
        {
            var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));
            var conductress = teleporters.Single(t => t.NpcID == 10050);
            Assert.Equal((100u, 6), (conductress.Fare, conductress.Destinations.Count));
            Assert.Equal(("Market", 1036u), (conductress.Destinations[5].Name, conductress.Destinations[5].MapID));
        }

        [Fact]
        public void TheShippedTalkerFileLoads()
        {
            var talkers = Talkers.Load(DataFiles.PathOf("talkers.json"));
            var ghoulKong = talkers.Single(t => t.NpcID == 4573);
            Assert.Equal(("In your dreams.", (byte)67), (ghoulKong.Reply, ghoulKong.Face));
        }

        [Fact]
        public void ShippedTalkersAndTeleportersAreNpcsWithoutAScript()
        {
            var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json")).Select(n => n.NpcID).ToHashSet();
            var ids = Talkers.Load(DataFiles.PathOf("talkers.json")).Select(t => t.NpcID)
                .Concat(Teleporters.Load(DataFiles.PathOf("teleporters.json")).Select(t => t.NpcID)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Contains(id, npcs));
            Assert.All(ids, id => Assert.False(Dialogs.Has(id), $"NPC {id} has a script"));
        }

        [Theory]
        [InlineData(1003u, 1004u)]
        [InlineData(1004u, 1003u)]
        [InlineData(2001u, 2002u)]
        [InlineData(2002u, 2001u)]
        [InlineData(999u, 0u)]
        public void SurgeonMiracleSwapsBodiesWithinAGender(uint body, uint other) =>
            Assert.Equal(other, Market.OtherBody(body));
    }
}

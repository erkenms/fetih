namespace Comet.Game.Tests.Npcs
{
    using System;
    using System.Linq;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Xunit;

    public class ShopTests
    {
        [Fact]
        public void ShopDatSectionsBecomeShops()
        {
            var shops = ShopTable.Parse(Encoding.Latin1.GetString(Client.ItemFiles.ShopDat(
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
        public void OpenShopLayout()
        {
            var shop = MsgAction.OpenShop(1000001, 5, 452, 336).Encode();
            Assert.Equal((32u, 5u, (ushort)160, (ushort)452, (ushort)336), (BitConverter.ToUInt32(shop, 8),
                BitConverter.ToUInt32(shop, 16), BitConverter.ToUInt16(shop, 20), BitConverter.ToUInt16(shop, 24),
                BitConverter.ToUInt16(shop, 26)));
        }

        [Fact]
        public void TheShippedTeleporterFileLoads()
        {
            var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));
            var conductress = teleporters.Single(t => t.NpcID == 10050);
            Assert.Equal((100u, 6), (conductress.Fare, conductress.Destinations.Count));
            Assert.Equal(("Market", 1036u), (conductress.Destinations[5].Name, conductress.Destinations[5].MapID));
        }
    }
}

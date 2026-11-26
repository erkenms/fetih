namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    // The test Shop.dat has Twin City's Blacksmith sell blades and glaives, and its
    // Pharmacist potions; every test item costs ItemFiles.TestPrice
    public class ShopTests : IntegrationTest
    {
        private const uint Blacksmith = ServerFixture.Blacksmith, Pharmacist = ServerFixture.Pharmacist;

        public ShopTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task TalkingToAShopkeeperOpensTheShop()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 336)).Player;
            await player.Game.TalkToNpcAsync(Blacksmith);
            var open = await player.Game.WaitForActionAsync(ActionMessage.OpenShop, player.User.Id);
            Assert.Equal((32u, Blacksmith), (open?.Command, open?.Timestamp));
            await SyncAsync(player);
            Assert.Null(await player.Game.ReadDialogAsync(Already));
        }

        [Fact]
        public async Task BuyingTakesSilverAndGivesTheItems()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 337)).Player;
            await player.Game.BuyAsync(Blacksmith, ServerFixture.Blade.Id, 2);
            Assert.NotNull(await SilverAsync(player, 1000 - 2 * ItemFiles.TestPrice));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id));

            var bought = await this.Servers.WaitForEventAsync("game", "item.bought", "name", player.User.Name);
            Assert.Equal(Blacksmith, bought.GetProperty("npcId").GetUInt32());
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.Blade.Id));
            Assert.Equal(2L, (long)count);
        }

        [Fact]
        public async Task ShopsSellOnlyTheirOwnItems()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 338)).Player;
            await player.Game.BuyAsync(Blacksmith, ServerFixture.Potion.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.shop_rejected", "name", player.User.Name);
            Assert.Equal("not_sold_here", refused.GetProperty("reason").GetString());
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForItemAsync(_ => true, Already));
        }

        [Fact]
        public async Task BuyingNeedsTheSilver()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 339, GameMaster)).Player;
            await CommandAsync(player, "/silver -990");
            Assert.NotNull(await SilverAsync(player, 10));
            await player.Game.BuyAsync(Pharmacist, ServerFixture.Potion.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.shop_rejected", "name", player.User.Name);
            Assert.Equal("not_enough_silver", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task ShopkeepersMustBeInView()
        {
            await using var player = (await this.PlayingAtAsync(1002, 600, 600)).Player;
            await player.Game.BuyAsync(Blacksmith, ServerFixture.Blade.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "npc.rejected", "name", player.User.Name);
            Assert.Equal(("too_far", "buy"), (refused.GetProperty("reason").GetString(), refused.GetProperty("use").GetString()));
        }

        [Fact]
        public async Task SellingGivesBackAThirdOfThePrice()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 340)).Player;
            await player.Game.BuyAsync(Blacksmith, ServerFixture.Blade.Id);
            var blade = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            Assert.NotNull(blade);
            Assert.NotNull(await SilverAsync(player, 1000 - ItemFiles.TestPrice));

            await player.Game.SellAsync(Blacksmith, blade.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, blade.Id));
            Assert.NotNull(await SilverAsync(player, 1000 - ItemFiles.TestPrice + ItemFiles.TestPrice / 3));
            var sold = await this.Servers.WaitForEventAsync("game", "item.sold", "name", player.User.Name);
            Assert.Equal(blade.Id, sold.GetProperty("itemId").GetUInt32());
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE ItemID = @id", ("@id", blade.Id));
            Assert.Equal(0L, (long)count);
        }
    }
}

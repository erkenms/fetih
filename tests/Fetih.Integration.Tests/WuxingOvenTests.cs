namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    // Composing at the Wuxing Oven and enchanting at Ethereal, in the market
    public class WuxingOvenTests : IntegrationTest
    {
        private const uint Market = ServerFixture.Market, Oven = 35016, Ethereal = 35015;

        public WuxingOvenTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task TheOvenOpensTheComposeWindowAndEtherealTheEnchantOne()
        {
            await using var player = (await this.PlayingAtAsync(Market, 318, 226)).Player;
            await player.Game.TalkToNpcAsync(Oven);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            var window = await player.Game.WaitForActionAsync(ActionMessage.OpenWindow, player.User.Id);
            Assert.Equal(ActionMessage.ComposeWindow, window?.Command);

            await player.Game.TalkToNpcAsync(Ethereal);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(2);
            var command = await player.Game.WaitForActionAsync(ActionMessage.ClientCommand, player.User.Id);
            Assert.Equal(ActionMessage.EnchantWindow, command?.Command);
        }

        [Fact]
        public async Task TwoPlusOneStonesMakeAPlusOneHelmet()
        {
            await using var player = (await this.PlayingAtAsync(Market, 318, 226, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var first = await GiveAsync(player, ServerFixture.PlusOneStone.Id);
            await player.Game.ComposeAsync(helmet.Id, first.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, first.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Plus == 0 && i.PlusProgress == 10));

            var second = await GiveAsync(player, ServerFixture.PlusOneStone.Id);
            await player.Game.ComposeAsync(helmet.Id, second.Id);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Plus == 1 && i.PlusProgress == 0));
            var saved = await this.Servers.QueryGameScalarAsync(
                "SELECT Plus * 1000 + PlusProgress FROM item WHERE ItemID = @id", ("@id", helmet.Id));
            Assert.Equal(1000L, Convert.ToInt64(saved));
        }

        [Fact]
        public async Task OneComposeCanBurnSeveralMinors()
        {
            await using var player = (await this.PlayingAtAsync(Market, 322, 226, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var first = await GiveAsync(player, ServerFixture.PlusOneStone.Id);
            var second = await GiveAsync(player, ServerFixture.PlusOneStone.Id);
            await player.Game.ComposeAsync(helmet.Id, first.Id, second.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, first.Id));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, second.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Plus == 1));
        }

        [Fact]
        public async Task ComposingNeedsTheOvenAndAMinorOfTheSameKind()
        {
            await using var player = (await this.PlayingAtAsync(Market, 180, 184, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var stone = await GiveAsync(player, ServerFixture.PlusOneStone.Id);
            await player.Game.ComposeAsync(helmet.Id, stone.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.compose_rejected", "name", player.User.Name);
            Assert.Equal("too_far", refused.GetProperty("reason").GetString());

            await CommandAsync(player, "/tp market 318 226");
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.ComposeAsync(helmet.Id, blade.Id);
            refused = await this.Servers.WaitForEventAsync("game", "item.compose_rejected", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("reason").GetString() != "too_far");
            Assert.Equal("minor_plus", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task EnchantingKeepsTheHigherRoll()
        {
            await using var player = (await this.PlayingAtAsync(Market, 318, 226, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var dragon = await GiveAsync(player, ServerFixture.SuperDragonGem.Id);
            await player.Game.EnchantAsync(helmet.Id, dragon.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, dragon.Id));
            var roll = await player.Game.WaitForItemActionAsync(ItemMessage.Enchant, helmet.Id);
            Assert.InRange(roll.Data, 200u, 255u);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Enchant == roll.Data));

            var tortoise = await GiveAsync(player, ServerFixture.NormalTortoiseGem.Id);
            await player.Game.EnchantAsync(helmet.Id, tortoise.Id);
            var lower = await player.Game.WaitForItemActionAsync(ItemMessage.Enchant, helmet.Id);
            Assert.InRange(lower.Data, 1u, 59u);
            await SyncAsync(player);
            var saved = await this.Servers.QueryGameScalarAsync(
                "SELECT Enchant FROM item WHERE ItemID = @id", ("@id", helmet.Id));
            Assert.Equal((long)roll.Data, Convert.ToInt64(saved));
        }
    }
}

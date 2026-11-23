namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    // The market's NPCs at their places in Data/npcs.json
    public class MarketNpcTests : IntegrationTest
    {
        private const uint Market = ServerFixture.Market, TwinCity = 1002;
        private const uint MarkController = 45, CPAdmin = 2071;
        private const uint ConquerPoints = 29;

        public MarketNpcTests(ServerFixture servers) : base(servers) { }

        private static string Text(System.Collections.Generic.List<DialogMessage> page) =>
            page == null ? null : string.Concat(page.Where(p => p.Action == DialogMessage.Line).Select(p => p.Text));

        [Fact]
        public async Task MarkControllerSendsPlayersBackWhereTheyCameFrom()
        {
            await using var player = (await this.PlayingAtAsync(TwinCity, 436, 436, GameMaster)).Player;
            await CommandAsync(player, "/tp market 214 218");
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));

            await player.Game.TalkToNpcAsync(MarkController);
            Assert.Contains("leave the market", Text(await player.Game.ReadDialogAsync()));
            await player.Game.AnswerNpcAsync(1);
            var back = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((TwinCity, (ushort)436, (ushort)436), (back?.Command, back?.X, back?.Y));
        }

        [Fact]
        public async Task MarkControllerSendsPlayersWhoLoggedInThereToTwinCity()
        {
            await using var player = (await this.PlayingAtAsync(Market, 214, 218)).Player;
            await player.Game.TalkToNpcAsync(MarkController);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            var back = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((TwinCity, (ushort)428, (ushort)378), (back?.Command, back?.X, back?.Y));
        }

        [Fact]
        public async Task CPAdminPaysForADragonBall()
        {
            await using var player = (await this.PlayingAtAsync(Market, 180, 184, GameMaster)).Player;
            await player.Game.TalkToNpcAsync(CPAdmin);
            Assert.Contains("215 CPs", Text(await player.Game.ReadDialogAsync()));
            await player.Game.AnswerNpcAsync(1);
            Assert.Contains("do not have a DragonBall", Text(await player.Game.ReadDialogAsync()));

            var ball = await GiveAsync(player, ServerFixture.DragonBall.Id);
            await player.Game.TalkToNpcAsync(CPAdmin);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, ball.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(
                a => a.Values.TryGetValue(ConquerPoints, out var v) && v == 215));
            Assert.Contains("enjoy your new CPs", Text(await player.Game.ReadDialogAsync()));
        }
    }
}

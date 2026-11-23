namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class MiningTests : IntegrationTest
    {
        private const uint Mine = ServerFixture.TwinCityMine;

        public MiningTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task MinersWithAPickAxeDigWhereOthersSeeIt()
        {
            await using var miner = (await this.PlayingAtAsync(Mine, 60, 60, GameMaster, level: 15)).Player;
            await using var watcher = (await this.PlayingAtAsync(Mine, 62, 60)).Player;
            var pick = await GiveAsync(miner, ServerFixture.PickAxe.Id);
            await miner.Game.EquipAsync(pick.Id, 4);
            Assert.NotNull(await miner.Game.WaitForItemActionAsync(ItemMessage.Equipped, pick.Id));

            await miner.Game.MineAsync(miner.User.Id, 60, 60);
            Assert.NotNull(await watcher.Game.WaitForActionAsync(ActionMessage.Mine, miner.User.Id));
            await this.Servers.WaitForEventAsync("game", "mining.started", "name", miner.User.Name);
        }

        [Fact]
        public async Task MiningNeedsAPickAxeAndAMine()
        {
            await using var player = (await this.PlayingAtAsync(Mine, 80, 80, level: 15)).Player;
            await player.Game.MineAsync(player.User.Id, 80, 80);
            var refused = await this.Servers.WaitForEventAsync("game", "mining.rejected", "name", player.User.Name);
            Assert.Equal("no_pick", refused.GetProperty("reason").GetString());

            await using var outside = (await this.PlayingAtAsync(1002, 480, 480, level: 15)).Player;
            await outside.Game.MineAsync(outside.User.Id, 480, 480);
            refused = await this.Servers.WaitForEventAsync("game", "mining.rejected", "name", outside.User.Name);
            Assert.Equal("not_a_mine", refused.GetProperty("reason").GetString());
        }
    }
}

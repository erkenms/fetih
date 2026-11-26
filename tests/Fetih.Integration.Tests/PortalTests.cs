namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class PortalTests : IntegrationTest
    {
        public PortalTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task SteppingNextToAPortalTakesThePlayerWhereItLeads()
        {
            var (px, py, _) = ServerFixture.StablePortal;
            await using var left = (await this.PlayingAtAsync(1002, px + 3, py)).Player;
            await using var waiting = (await this.PlayingAtAsync(1006, 40, 31)).Player;
            await using var traveller = (await this.PlayingAtAsync(1002, px + 1, py + 1)).Player;
            Assert.NotNull(await left.Game.WaitForSpawnAsync(traveller.User.Id));

            await traveller.Game.UsePortalAsync(traveller.User.Id, px + 1, py + 1);
            var arrived = await traveller.Game.WaitForActionAsync(ActionMessage.Teleport, traveller.User.Id);
            Assert.NotNull(arrived);
            Assert.Equal(1006u, arrived.Command);
            Assert.Equal((37, 31), (arrived.X, arrived.Y));

            Assert.True(await left.Game.WaitForRemovalAsync(traveller.User.Id));
            Assert.True(await traveller.Game.WaitForRemovalAsync(left.User.Id));
            var seen = await waiting.Game.WaitForSpawnAsync(traveller.User.Id);
            Assert.NotNull(seen);
            Assert.Equal((37, 31), (seen.X, seen.Y));
            Assert.NotNull(await traveller.Game.WaitForSpawnAsync(waiting.User.Id));

            await this.Servers.WaitForEventAsync("game", "move.teleport", "name", traveller.User.Name);
            Assert.Equal((1006u, 37, 31), await this.SavedPositionAsync(traveller));
        }

        [Theory]
        [InlineData(650, 650, "no_portal")]
        [InlineData(701, 699, "no_destination")]
        public async Task APortalRequestThatLeadsNowherePutsThePlayerBack(int x, int y, string reason)
        {
            await using var player = (await this.PlayingAtAsync(1002, x, y)).Player;
            await player.Game.UsePortalAsync(player.User.Id, x, y);
            var back = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.NotNull(back);
            Assert.Equal(1002u, back.Command);
            Assert.Equal((x, y), (back.X, back.Y));
            var rejected = await this.Servers.WaitForEventAsync("game", "move.rejected", "name", player.User.Name);
            Assert.Equal(reason, rejected.GetProperty("reason").GetString());
            Assert.Equal((1002u, x, y), await this.SavedPositionAsync(player));
        }
    }
}

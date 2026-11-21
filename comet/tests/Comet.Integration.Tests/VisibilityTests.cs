namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Xunit;

    public class VisibilityTests : IntegrationTest
    {
        public VisibilityTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task PlayersNearEachOtherSeeEachOther()
        {
            await using var first = (await this.PlayingAtAsync(1002, 300, 300)).Player;
            await using var second = (await this.PlayingAtAsync(1002, 310, 288)).Player;

            var seenByFirst = await first.Game.WaitForSpawnAsync(second.User.Id);
            Assert.NotNull(seenByFirst);
            Assert.Equal(second.User.Name, seenByFirst.Name);
            Assert.Equal((310, 288), (seenByFirst.X, seenByFirst.Y));
            Assert.Equal(second.User.Mesh, seenByFirst.Mesh);
            Assert.Equal(second.User.Level, seenByFirst.Level);

            // The one who arrived later sees the one already there
            var seenBySecond = await second.Game.WaitForSpawnAsync(first.User.Id);
            Assert.NotNull(seenBySecond);
            Assert.Equal((300, 300), (seenBySecond.X, seenBySecond.Y));
        }

        [Fact]
        public async Task PlayersOnDifferentMapsDontSeeEachOther()
        {
            await using var first = (await this.PlayingAtAsync(1002, 520, 600)).Player;
            await using var second = (await this.PlayingAtAsync(1006, 20, 20)).Player;
            // The second's login is done once the first hears from it
            await SyncAsync(second, first);
            Assert.Null(await first.Game.WaitForSpawnAsync(second.User.Id, Already));
        }

        [Fact]
        public async Task PlayerWhoLeavesIsRemovedFromScreens()
        {
            await using var watcher = (await this.PlayingAtAsync(1002, 700, 700)).Player;
            await using var leaver = (await this.PlayingAtAsync(1002, 705, 705)).Player;
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(leaver.User.Id));

            await leaver.DisposeAsync();
            Assert.True(await watcher.Game.WaitForRemovalAsync(leaver.User.Id));
        }

        [Fact]
        public async Task PlayerWhoLogsInAgainAppearsAgain()
        {
            await using var watcher = (await this.PlayingAtAsync(1002, 800, 800)).Player;
            var (first, email) = await this.PlayingAtAsync(1002, 801, 801);
            await using var firstGuard = first;
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(first.User.Id));

            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(again.User.Id));
        }
    }
}

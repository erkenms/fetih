namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Xunit;

    public class VisibilityTests : IntegrationTest
    {
        public VisibilityTests(ServerFixture servers) : base(servers) { }

        // Long enough for a spawn that was going to arrive to have arrived
        private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);

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
        public async Task PlayersFartherThan18CellsDontSeeEachOther()
        {
            await using var first = (await this.PlayingAtAsync(1002, 500, 500)).Player;
            await using var second = (await this.PlayingAtAsync(1002, 519, 500)).Player;
            Assert.Null(await first.Game.WaitForSpawnAsync(second.User.Id, Quiet));
            Assert.Null(await second.Game.WaitForSpawnAsync(first.User.Id, Quiet));
        }

        [Fact]
        public async Task PlayersOnDifferentMapsDontSeeEachOther()
        {
            await using var first = (await this.PlayingAtAsync(1002, 61, 109)).Player;
            await using var second = (await this.PlayingAtAsync(1010, 61, 109)).Player;
            Assert.Null(await first.Game.WaitForSpawnAsync(second.User.Id, Quiet));
        }

        [Fact]
        public async Task PlayerWhoLeavesIsRemovedFromScreens()
        {
            await using var watcher = (await this.PlayingAtAsync(1002, 700, 700)).Player;
            var leaver = (await this.PlayingAtAsync(1002, 705, 705)).Player;
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

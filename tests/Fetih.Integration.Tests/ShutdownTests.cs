namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Xunit;

    public class ShutdownTests : IntegrationTest
    {
        public ShutdownTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task SigtermSavesOnlinePlayersBeforeTheServerExits()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 300, 300);
            await using (player)
            {
                // A step is only in memory until the next save
                await player.Game.WalkAsync(player.User.Id, 6);
                Assert.NotNull(await player.Game.WaitForWalkAsync(player.User.Id));

                int mark = this.Servers.Mark();
                var stopped = await this.Servers.RestartGameAsync();

                Assert.True(stopped < TimeSpan.FromSeconds(20), $"The game took {stopped} to stop");
                var save = await this.Servers.WaitForLogoutSaveAsync(player.User.Name, mark);
                Assert.Equal(301, save.GetProperty("x").GetInt32());
                await this.Servers.WaitForEventAsync("game", "server.shutdown",
                    e => e.GetProperty("msg").GetString().StartsWith("Logged out"), after: mark);
                Assert.True(await player.Game.WaitClosedAsync());
            }

            // The restarted server loads the saved position
            await using var again = await this.Servers.PlayAsync(email, Password, null);
            Assert.Equal(301, (int)again.Spawn.X);
        }
    }
}

namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Xunit;

    public class WeatherTests : IntegrationTest
    {
        public WeatherTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task AGameMasterSetsTheWeatherOfTheirMap()
        {
            await using var gm = (await this.PlayingAtAsync(1002, 300, 300, GameMaster)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 302, 300)).Player;
            try
            {
                await CommandAsync(gm, "/weather leaves 50 -10 2");
                Assert.Equal((5u, 50u, -10), await watcher.Game.WaitForWeatherAsync());

                await using var late = (await this.PlayingAtAsync(1002, 304, 300)).Player;
                Assert.Equal((5u, 50u, -10), await late.Game.WaitForWeatherAsync());
            }
            finally
            {
                await CommandAsync(gm, "/weather none");
            }
            Assert.Equal((0u, 100u, 0), await watcher.Game.WaitForWeatherAsync());
        }

        [Fact]
        public async Task AMapWithoutWeatherSendsNone()
        {
            await using var player = (await this.PlayingAtAsync(1002, 310, 300)).Player;
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForWeatherAsync(Already));
        }
    }
}

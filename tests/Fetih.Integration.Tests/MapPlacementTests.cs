namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Xunit;

    public class MapPlacementTests : IntegrationTest
    {
        public MapPlacementTests(ServerFixture servers) : base(servers) { }

        // Saved where they can't stand: moved to the start, 1002 at 428,378
        [Theory]
        [InlineData(9999u, 50, 50, "unknown_map")]
        [InlineData(1002u, 100, 100, "blocked_cell")]
        [InlineData(1002u, 5000, 10, "blocked_cell")]
        public async Task CharacterWhereTheyCantStandIsMovedToTheStart(uint map, int x, int y, string reason)
        {
            await using var player = (await this.PlayingAtAsync(map, x, y)).Player;
            Assert.Equal((1002u, 428, 378), (player.Spawn.Command, (int)player.Spawn.X, (int)player.Spawn.Y));
            var relocated = await this.Servers.WaitForEventAsync(
                "game", "character.relocated", "name", player.User.Name);
            Assert.Equal((reason, map, x, y), (relocated.GetProperty("reason").GetString(),
                relocated.GetProperty("fromMapId").GetUInt32(),
                relocated.GetProperty("fromX").GetInt32(), relocated.GetProperty("fromY").GetInt32()));
        }

        [Fact]
        public async Task LeavingTheMarketSavesItsEntrance()
        {
            var (player, email) = await this.PlayingAtAsync(ServerFixture.Market, 150, 150);
            await this.LogOutAsync(player);
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(ServerFixture.Market, again.Spawn.Command);
            Assert.Equal((303, 237), (again.Spawn.X, again.Spawn.Y));
        }
    }
}

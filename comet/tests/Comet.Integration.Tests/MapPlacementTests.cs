namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Xunit;

    public class MapPlacementTests : IntegrationTest
    {
        public MapPlacementTests(ServerFixture servers) : base(servers) { }

        private async Task<Comet.Client.Player> ReturnAtAsync(uint mapID, int x, int y) =>
            (await this.PlayingAtAsync(mapID, x, y)).Player;

        [Fact]
        public async Task CharacterAppearsWhereTheyWereSaved()
        {
            await using var player = await this.ReturnAtAsync(1002, 300, 280);
            Assert.Equal(1002u, player.Spawn.Command);
            Assert.Equal((300, 280), (player.Spawn.X, player.Spawn.Y));
        }

        [Fact]
        public async Task CharacterOnAnUnknownMapIsMovedToTheStart()
        {
            await using var player = await this.ReturnAtAsync(9999, 50, 50);
            Assert.Equal(1010u, player.Spawn.Command);
            Assert.Equal((61, 109), (player.Spawn.X, player.Spawn.Y));
            var relocated = await this.Servers.WaitForEventAsync(
                "game", "character.relocated", "name", player.User.Name);
            Assert.Equal("unknown_map", relocated.GetProperty("reason").GetString());
            Assert.Equal(9999u, relocated.GetProperty("fromMapId").GetUInt32());
        }

        [Fact]
        public async Task CharacterOnABlockedCellIsMovedToTheStart()
        {
            var (x, y) = ServerFixture.BlockedCell;
            await using var player = await this.ReturnAtAsync(1002, x, y);
            Assert.Equal(1010u, player.Spawn.Command);
            var relocated = await this.Servers.WaitForEventAsync(
                "game", "character.relocated", "name", player.User.Name);
            Assert.Equal("blocked_cell", relocated.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task CharacterOutsideTheMapIsMovedToTheStart()
        {
            await using var player = await this.ReturnAtAsync(1002, 5000, 10);
            Assert.Equal(1010u, player.Spawn.Command);
        }
    }
}

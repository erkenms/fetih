namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class PortalTests : IntegrationTest
    {
        public PortalTests(ServerFixture servers) : base(servers) { }

        private async Task<(uint Map, int X, int Y)> SavedPositionAsync(Player player)
        {
            var name = player.User.Name;
            bool IsSave(JsonElement e) =>
                e.TryGetProperty("event", out var ev) && ev.GetString() == "character.saved" &&
                e.GetProperty("name").GetString() == name && e.GetProperty("reason").GetString() == "logout";

            int before = this.Servers.Events("game").Count(IsSave);
            await player.DisposeAsync();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (true)
            {
                var saves = this.Servers.Events("game").Where(IsSave).ToList();
                if (saves.Count > before)
                    return (saves[^1].GetProperty("mapId").GetUInt32(),
                        saves[^1].GetProperty("x").GetInt32(), saves[^1].GetProperty("y").GetInt32());
                Assert.True(DateTime.UtcNow < deadline, $"{name} wasn't saved after logging out");
                await Task.Delay(50);
            }
        }

        [Fact]
        public async Task SteppingNextToAPortalTakesThePlayerWhereItLeads()
        {
            var (px, py, _) = ServerFixture.StablePortal;
            await using var left = (await this.PlayingAtAsync(1002, px + 3, py)).Player;
            await using var waiting = (await this.PlayingAtAsync(1006, 40, 31)).Player;
            var traveller = (await this.PlayingAtAsync(1002, px + 1, py + 1)).Player;
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
            var player = (await this.PlayingAtAsync(1002, x, y)).Player;
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

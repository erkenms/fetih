namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class MovementTests : IntegrationTest
    {
        public MovementTests(ServerFixture servers) : base(servers) { }

        // Direction 6 is +X, 2 is -X, 0 is +Y, 4 is -Y
        private const int East = 6, West = 2, South = 0, North = 4;

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player) =>
            this.Servers.WaitForEventAsync("game", "move.rejected", "name", player.User.Name);

        [Fact]
        public async Task WalkingMovesTheCharacterAndOthersSeeTheStep()
        {
            await using var walker = (await this.PlayingAtAsync(1002, 300, 300)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 305, 300)).Player;
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(walker.User.Id));

            await walker.Game.WalkAsync(walker.User.Id, East);
            var seen = await watcher.Game.WaitForWalkAsync(walker.User.Id);
            Assert.NotNull(seen);
            Assert.Equal(East, (int)(seen.Direction % 8));
            Assert.NotNull(await walker.Game.WaitForWalkAsync(walker.User.Id));

            await walker.Game.WalkAsync(walker.User.Id, South);
            Assert.NotNull(await watcher.Game.WaitForWalkAsync(walker.User.Id));
            Assert.Equal((1002u, 301, 301), await this.SavedPositionAsync(walker));
        }

        [Fact]
        public async Task WalkingIntoABlockedCellPutsThePlayerBack()
        {
            var (bx, by) = ServerFixture.BlockedCell;
            await using var player = (await this.PlayingAtAsync(1002, bx, by + 1)).Player;

            await player.Game.WalkAsync(player.User.Id, North);
            var back = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.NotNull(back);
            Assert.Equal(1002u, back.Command);
            Assert.Equal((bx, by + 1), (back.X, back.Y));
            Assert.Equal("blocked_cell", (await this.RejectedAsync(player)).GetProperty("reason").GetString());
            Assert.Equal((1002u, bx, by + 1), await this.SavedPositionAsync(player));
        }

        [Fact]
        public async Task WalkingOffTheMapPutsThePlayerBack()
        {
            await using var player = (await this.PlayingAtAsync(1002, 0, 50)).Player;
            await player.Game.WalkAsync(player.User.Id, West);
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            Assert.Equal("outside_map", (await this.RejectedAsync(player)).GetProperty("reason").GetString());
        }

        [Fact]
        public async Task StepWithAnotherCharactersIdIsRefused()
        {
            await using var player = (await this.PlayingAtAsync(1002, 200, 200)).Player;
            await player.Game.WalkAsync(player.User.Id + 1, East);
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            Assert.Equal("wrong_id", (await this.RejectedAsync(player)).GetProperty("reason").GetString());
            Assert.Equal((1002u, 200, 200), await this.SavedPositionAsync(player));
        }

        [Fact]
        public async Task JumpingUpTo16CellsMovesTheCharacterAndOthersSeeIt()
        {
            await using var jumper = (await this.PlayingAtAsync(1002, 400, 400)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 405, 405)).Player;
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(jumper.User.Id));

            await jumper.Game.JumpAsync(jumper.User.Id, 400, 400, 416, 390);
            var seen = await watcher.Game.WaitForActionAsync(ActionMessage.Jump, jumper.User.Id);
            Assert.NotNull(seen);
            Assert.Equal((uint)(416 | (390 << 16)), seen.Command);
            Assert.NotNull(await jumper.Game.WaitForActionAsync(ActionMessage.Jump, jumper.User.Id));
            Assert.Equal((1002u, 416, 390), await this.SavedPositionAsync(jumper));
        }

        [Fact]
        public async Task JumpingFartherThan16CellsIsRefused()
        {
            await using var player = (await this.PlayingAtAsync(1002, 450, 450)).Player;
            await player.Game.JumpAsync(player.User.Id, 450, 450, 467, 450);
            var back = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((450, 450), (back.X, back.Y));
            Assert.Equal("too_far", (await this.RejectedAsync(player)).GetProperty("reason").GetString());
            Assert.Equal((1002u, 450, 450), await this.SavedPositionAsync(player));
        }

        [Fact]
        public async Task JumpingOntoABlockedCellIsRefused()
        {
            var (bx, by) = ServerFixture.BlockedCell;
            await using var player = (await this.PlayingAtAsync(1002, bx + 5, by + 5)).Player;
            await player.Game.JumpAsync(player.User.Id, bx + 5, by + 5, bx, by);
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            Assert.Equal("blocked_cell", (await this.RejectedAsync(player)).GetProperty("reason").GetString());
        }

        [Fact]
        public async Task WalkingOutOfViewAndBackRemovesAndShowsBothWays()
        {
            // 18 cells apart: just in view
            await using var walker = (await this.PlayingAtAsync(1002, 600, 600)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 618, 600)).Player;
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(walker.User.Id));
            Assert.NotNull(await walker.Game.WaitForSpawnAsync(watcher.User.Id));

            await walker.Game.WalkAsync(walker.User.Id, West);
            Assert.True(await watcher.Game.WaitForRemovalAsync(walker.User.Id));
            Assert.True(await walker.Game.WaitForRemovalAsync(watcher.User.Id));

            await walker.Game.WalkAsync(walker.User.Id, East);
            var again = await watcher.Game.WaitForSpawnAsync(walker.User.Id);
            Assert.NotNull(again);
            Assert.Equal((600, 600), (again.X, again.Y));
            Assert.NotNull(await walker.Game.WaitForSpawnAsync(watcher.User.Id));
        }

        [Fact]
        public async Task JumpingIntoViewShowsBothWays()
        {
            await using var jumper = (await this.PlayingAtAsync(1002, 700, 700)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 730, 700)).Player;
            // The watcher's login sent who was in view
            await SyncAsync(watcher);
            Assert.Null(await watcher.Game.WaitForSpawnAsync(jumper.User.Id, Already));

            await jumper.Game.JumpAsync(jumper.User.Id, 700, 700, 715, 700);
            var seen = await watcher.Game.WaitForSpawnAsync(jumper.User.Id);
            Assert.NotNull(seen);
            Assert.Equal((715, 700), (seen.X, seen.Y));
            Assert.NotNull(await jumper.Game.WaitForSpawnAsync(watcher.User.Id));
        }

        [Fact]
        public async Task TurningAndEmotesGoToOthersAndShowToNewcomers()
        {
            await using var actor = (await this.PlayingAtAsync(1002, 800, 800)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, 802, 802)).Player;
            Assert.NotNull(await watcher.Game.WaitForSpawnAsync(actor.User.Id));

            await actor.Game.TurnAsync(actor.User.Id, 3);
            var turn = await watcher.Game.WaitForActionAsync(ActionMessage.Turn, actor.User.Id);
            Assert.Equal(3, turn.Direction);

            await actor.Game.EmoteAsync(actor.User.Id, 250, 5);
            var emote = await watcher.Game.WaitForActionAsync(ActionMessage.Emote, actor.User.Id);
            Assert.Equal(250u, emote.Command);

            // Not sent back to the one who did it
            await SyncAsync(actor);
            Assert.Null(await actor.Game.WaitForActionAsync(ActionMessage.Emote, actor.User.Id, Already));

            // Someone arriving later sees the emote and the direction
            await using var newcomer = (await this.PlayingAtAsync(1002, 804, 804)).Player;
            var spawn = await newcomer.Game.WaitForSpawnAsync(actor.User.Id);
            Assert.Equal(5, spawn.Direction);
            Assert.Equal(250u, spawn.Stance);

            // Moving ends the emote
            await actor.Game.WalkAsync(actor.User.Id, East);
            Assert.NotNull(await actor.Game.WaitForWalkAsync(actor.User.Id));
            await using var later = (await this.PlayingAtAsync(1002, 806, 806)).Player;
            Assert.Equal(0u, (await later.Game.WaitForSpawnAsync(actor.User.Id)).Stance);
        }
    }
}

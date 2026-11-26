namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    public class PkTests : IntegrationTest
    {
        private const uint JailMap = 6000, Warden = 42;
        private static readonly TimeSpan Slow = TimeSpan.FromSeconds(15);

        public PkTests(ServerFixture servers) : base(servers) { }

        private static Task<UserAttribMessage> AttribAsync(
            Player watcher, uint id, uint type, ulong value, TimeSpan? timeout = null) =>
            watcher.Game.WaitForAttribAsync(a =>
                a.Id == id && a.Values.TryGetValue(type, out var v) && v == value, timeout);

        [Fact]
        public async Task PkModeIsEchoedAndExplained()
        {
            await using var player = (await this.PlayingAtAsync(1002, 620, 640)).Player;
            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.PkMode, command: 0));
            var echo = await player.Game.WaitForActionAsync(ActionMessage.PkMode, player.User.Id);
            Assert.Equal(0u, echo?.Command);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.StartsWith("Free PK mode")));
            await this.Servers.WaitForEventAsync("game", "pk.mode", "name", player.User.Name);

            // An unknown mode is answered with the one the player is in
            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.PkMode, command: 9));
            echo = await player.Game.WaitForActionAsync(ActionMessage.PkMode, player.User.Id);
            Assert.Equal(0u, echo?.Command);
            await this.Servers.WaitForEventAsync("game", "pk.mode_rejected", "name", player.User.Name);
        }

        [Fact]
        public async Task NameColorsShowToEveryoneAndPointsAreKept()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 640, 640, GameMaster);
            await using var observer = (await this.PlayingAtAsync(1002, 642, 640)).Player;
            uint id = player.User.Id;

            await CommandAsync(player, "/pk 40");
            Assert.NotNull(await AttribAsync(player, id, PkPoints, 40));
            Assert.NotNull(await AttribAsync(player, id, Status, RedName));
            Assert.NotNull(await AttribAsync(observer, id, Status, RedName));

            await CommandAsync(player, "/pk 150");
            Assert.NotNull(await AttribAsync(player, id, Status, BlackName));
            Assert.NotNull(await AttribAsync(observer, id, Status, BlackName));
            var logged = await this.Servers.WaitForEventAsync("game", "pk.points",
                e => e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("points").GetInt32() == 150);
            Assert.Equal("gm", logged.GetProperty("reason").GetString());

            // Coming back: the points are saved, and others see the black name in the spawn
            await this.LogOutAsync(player);
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.InRange(again.User.PkPoints, 140, 150);
            Assert.NotNull(await observer.Game.WaitForSpawnAsync(id, s => s.Status == BlackName));
            Assert.NotNull(await AttribAsync(again, id, Status, BlackName));
        }

        [Fact]
        public async Task PointsGoDownWhileOnline()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 640, GameMaster)).Player;
            uint id = player.User.Id;
            await CommandAsync(player, "/pk 30");
            Assert.NotNull(await AttribAsync(player, id, Status, RedName));

            Assert.NotNull(await AttribAsync(player, id, PkPoints, 29, Slow));
            Assert.NotNull(await AttribAsync(player, id, Status, 0));
        }

        [Fact]
        public async Task FlashingEndsByItself()
        {
            await using var player = (await this.PlayingAtAsync(1002, 680, 640, GameMaster)).Player;
            await using var observer = (await this.PlayingAtAsync(1002, 682, 640)).Player;
            uint id = player.User.Id;

            await CommandAsync(player, "/flash");
            Assert.NotNull(await AttribAsync(player, id, Status, Flashing));
            Assert.NotNull(await AttribAsync(observer, id, Status, Flashing));
            await this.Servers.WaitForEventAsync("game", "pk.flashing", "name", player.User.Name);

            Assert.NotNull(await AttribAsync(player, id, Status, 0, Slow));
            Assert.NotNull(await AttribAsync(observer, id, Status, 0, Slow));
        }

        [Fact]
        public async Task BlackNamesPayTheWardenBail()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 640, GameMaster)).Player;
            uint id = player.User.Id;
            await CommandAsync(player, "/pk 150");
            Assert.NotNull(await AttribAsync(player, id, Status, BlackName));

            await CommandAsync(player, "/jail " + player.User.Name);
            var teleport = await player.Game.WaitForActionAsync(ActionMessage.Teleport, id);
            Assert.Equal((JailMap, (ushort)29, (ushort)72), (teleport?.Command, teleport?.X, teleport?.Y));
            var jailed = await this.Servers.WaitForEventAsync("game", "pk.jailed", "name", player.User.Name);
            Assert.Equal("gm", jailed.GetProperty("reason").GetString());

            await player.Game.TalkToNpcAsync(Warden);
            var page = await player.Game.ReadDialogAsync();
            Assert.Contains("500,000 silver", page?[0].Text);
            await player.Game.AnswerNpcAsync(1);
            page = await player.Game.ReadDialogAsync();
            Assert.Contains("You don't have", page?[0].Text);

            await CommandAsync(player, "/silver 500000");
            Assert.NotNull(await AttribAsync(player, id, Silver, 501000));
            await player.Game.TalkToNpcAsync(Warden);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await AttribAsync(player, id, Silver, 1000));
            teleport = await player.Game.WaitForActionAsync(ActionMessage.Teleport, id);
            Assert.Equal(1002u, teleport?.Command);
            var released = await this.Servers.WaitForEventAsync("game", "pk.released", "name", player.User.Name);
            Assert.Equal("bail", released.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task OthersLeaveJailForFree()
        {
            await using var player = (await this.PlayingAtAsync(JailMap, 30, 70)).Player;
            await player.Game.TalkToNpcAsync(Warden);
            var page = await player.Game.ReadDialogAsync();
            Assert.Contains("needn't stay", page?[0].Text);
            await player.Game.AnswerNpcAsync(1);
            var teleport = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal(1002u, teleport?.Command);
            var released = await this.Servers.WaitForEventAsync("game", "pk.released", "name", player.User.Name);
            Assert.Equal("free", released.GetProperty("reason").GetString());
        }
    }
}

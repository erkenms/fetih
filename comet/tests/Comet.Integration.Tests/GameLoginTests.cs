namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class GameLoginTests : IntegrationTest
    {
        public GameLoginTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task MadeUpTokenIsRefused()
        {
            await using var game = await this.Servers.ConnectGameAsync();
            var result = await game.LoginAsync(0x0123456789ABCDEF);
            Assert.Equal(InvalidLogin, result.Message);
            Assert.True(await game.WaitClosedAsync());
            await this.Servers.WaitForEventAsync("game", "game.auth_failed", "reason", "invalid_token");
        }

        [Fact]
        public async Task TokenWorksOnlyOnce()
        {
            var email = await this.Servers.CreateAccountAsync();
            var login = await this.Servers.LoginAsync(email, Password);
            await using (var first = await this.Servers.ConnectGameAsync())
                Assert.Equal(NewRole, (await first.LoginAsync(login.Token)).Message);

            await using var second = await this.Servers.ConnectGameAsync();
            Assert.Equal(InvalidLogin, (await second.LoginAsync(login.Token)).Message);
        }

        [Fact]
        public async Task NewAccountIsAskedToCreateACharacter()
        {
            var (game, _, _) = await this.AtCharacterCreationAsync();
            await using (game)
                await this.Servers.WaitForEventAsync("game", "net.connect", _ => true);
        }

        [Fact]
        public async Task ReturningPlayerGoesStraightToTheMap()
        {
            var (first, email) = await this.PlayingAsync();
            var id = first.User.Id;
            var name = first.User.Name;
            await first.DisposeAsync();
            await this.Servers.WaitForEventAsync("game", "session.logout", "name", name);

            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(id, again.User.Id);
            Assert.Equal(name, again.User.Name);
            Assert.Equal(1010u, again.Spawn.Command);
        }

        [Fact]
        public async Task OnlineCountGoesUpAndDownWithPlayers()
        {
            // Other tests' players may still be leaving, so compare two of ours
            var (first, _) = await this.PlayingAsync();
            await using var firstGuard = first;
            var (second, _) = await this.PlayingAsync();
            async Task<int> OnlineAtAsync(string eventName, string name) =>
                (await this.Servers.WaitForEventAsync("game", eventName, "name", name))
                    .GetProperty("online").GetInt32();

            int withBoth = await OnlineAtAsync("session.login", second.User.Name);
            Assert.True(withBoth >= 2);
            await second.DisposeAsync();
            Assert.True(await OnlineAtAsync("session.logout", second.User.Name) < withBoth);
        }

        [Fact]
        public async Task LoggingInAgainClosesTheFirstConnection()
        {
            var (first, email) = await this.PlayingAsync();
            await using (first)
            {
                await using var second = await this.Servers.PlayAsync(email, Password);
                Assert.Equal(first.User.Id, second.User.Id);
                Assert.True(await first.Game.WaitClosedAsync());
                await this.Servers.WaitForEventAsync("game", "session.logout",
                    e => e.GetProperty("name").GetString() == first.User.Name
                        && e.GetProperty("reason").GetString() == "replaced");
            }
        }

        [Fact]
        public async Task LoginAgainWhileInTheGameIsIgnored()
        {
            var (player, _) = await this.PlayingAsync();
            await using (player)
            {
                // Even a made-up token gets no answer once the character is in the game
                await player.Game.SendAsync(GameClient.BuildConnect(0x0123456789ABCDEF));
                await Assert.ThrowsAsync<TimeoutException>(() =>
                    player.Game.ReceiveAsync(PacketTypes.MsgTalk, TimeSpan.FromSeconds(1)));

                // The connection still works
                var spawn = await player.Game.EnterMapAsync(player.User.Id);
                Assert.Equal(1010u, spawn.Command);
            }
        }

        [Fact]
        public async Task ManyPlayersCanPlayAtOnce()
        {
            var players = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => this.PlayingAsync()));
            try
            {
                Assert.Equal(20, players.Select(p => p.Player.User.Id).Distinct().Count());
                Assert.All(players, p => Assert.Equal(1010u, p.Player.Spawn.Command));
            }
            finally
            {
                foreach (var (player, _) in players)
                    await player.DisposeAsync();
            }
        }
    }
}

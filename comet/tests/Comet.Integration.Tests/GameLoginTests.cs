namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using MySqlConnector;
    using Xunit;

    public class GameLoginTests : IntegrationTest
    {
        public GameLoginTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task MadeUpTokenIsRefused()
        {
            int mark = this.Servers.Mark();
            await using var game = await this.Servers.ConnectGameAsync();
            var result = await game.LoginAsync(0x0123456789ABCDEF);
            Assert.Equal(InvalidLogin, result.Message);
            Assert.True(await game.WaitClosedAsync());
            await this.Servers.WaitForEventAsync("game", "game.auth_failed", "reason", "invalid_token", mark);
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
                await SyncAsync(player);
                Assert.Null(await player.Game.WaitForTalkAsync(_ => true, Already));

                // The connection still works
                var (spawn, _) = await player.Game.EnterMapAsync(player.User.Id);
                Assert.Equal(1002u, spawn.Command);
            }
        }

        // The logout save runs after the connection is gone. While this test holds the
        // character's row the save can't finish, and a login that didn't wait for it would
        // load the position from before the step.
        [Fact]
        public async Task LoggingInWhileTheLogoutIsSavingWaitsForTheSave()
        {
            var (player, email) = await this.PlayingAsync();
            int x = player.Spawn.X;
            await player.Game.WalkAsync(player.User.Id, 6);
            Assert.NotNull(await player.Game.WaitForWalkAsync(player.User.Id));

            await using var db = await this.Servers.OpenGameDatabaseAsync();
            await using var hold = await db.BeginTransactionAsync();
            var row = new MySqlCommand("SELECT Name FROM `character` WHERE CharacterID = @id FOR UPDATE", db, hold);
            row.Parameters.AddWithValue("@id", player.User.Id);
            await row.ExecuteScalarAsync();

            // Gone from the server's sessions, so the new login can't find it there
            int mark = this.Servers.Mark();
            await player.DisposeAsync();
            await this.Servers.WaitForEventAsync("game", "session.logout", "name", player.User.Name, mark);

            var again = this.Servers.PlayAsync(email, Password);
            await Task.WhenAny(again, Task.Delay(500));
            await hold.RollbackAsync();
            await using var back = await again;
            Assert.Equal(x + 1, back.Spawn.X);
        }

        [Fact]
        public async Task ManyPlayersCanPlayAtOnce()
        {
            var players = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => this.PlayingAsync()));
            try
            {
                Assert.Equal(20, players.Select(p => p.Player.User.Id).Distinct().Count());
                Assert.All(players, p => Assert.Equal(1002u, p.Player.Spawn.Command));
            }
            finally
            {
                foreach (var (player, _) in players)
                    await player.DisposeAsync();
            }
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Diagnostics;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class ConnectionLifetimeTests : IntegrationTest
    {
        public ConnectionLifetimeTests(ServerFixture servers) : base(servers) { }

        private static readonly TimeSpan LoginTimeout =
            TimeSpan.FromSeconds(ServerFixture.LoginTimeoutSeconds);

        private static async Task<TimeSpan> LifetimeAsync(int port, byte[] send = null)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(ServerFixture.Host, port);
            var watch = Stopwatch.StartNew();
            var stream = tcp.GetStream();
            if (send != null)
                await stream.WriteAsync(send);

            var buffer = new byte[4096];
            var deadline = Task.Delay(LoginTimeout + TimeSpan.FromSeconds(10));
            try
            {
                while (true)
                {
                    var read = stream.ReadAsync(buffer).AsTask();
                    if (await Task.WhenAny(read, deadline) == deadline)
                        throw new Xunit.Sdk.XunitException("The server kept the connection open");
                    if (await read == 0)
                        break;
                }
            }
            catch (System.IO.IOException) { }
            return watch.Elapsed;
        }

        private async Task<TimeSpan> ExchangedLifetimeAsync()
        {
            var watch = Stopwatch.StartNew();
            await using var game = await this.Servers.ConnectGameAsync();
            Assert.True(await game.WaitClosedAsync(LoginTimeout + TimeSpan.FromSeconds(10)),
                "The server kept a connection open that never logged in");
            return watch.Elapsed;
        }

        [Fact]
        public async Task ConnectionsThatDontLogInAreDropped()
        {
            var lifetimes = await Task.WhenAll(
                LifetimeAsync(this.Servers.GamePort),                       // silent
                this.ExchangedLifetimeAsync(),                              // key exchange only
                LifetimeAsync(this.Servers.AccountPort),                    // silent
                LifetimeAsync(this.Servers.AccountPort, new byte[] { 1 })); // one byte

            foreach (var lifetime in lifetimes)
                Assert.True(lifetime >= LoginTimeout - TimeSpan.FromSeconds(0.5),
                    $"Dropped after {lifetime}, before the login timeout");
        }

        [Fact]
        public async Task LoggedInPlayersAreNeverDroppedForIdling()
        {
            var (creating, token, _) = await this.AtCharacterCreationAsync();
            await using var creatingGuard = creating;
            var (playing, _) = await this.PlayingAsync();
            await using var playingGuard = playing;

            // Both connections opened more than the login timeout ago by the end of this
            await Task.Delay(LoginTimeout + TimeSpan.FromSeconds(1));
            Assert.False(creating.Closed);
            Assert.False(playing.Game.Closed);

            var name = this.Servers.NewCharacterName();
            Assert.Equal(AnswerOk, await creating.CreateCharacterAsync(name, 1003, 10, (uint)token));
            var spawn = await playing.Game.EnterMapAsync(playing.User.Id);
            Assert.Equal(1010u, spawn.Command);
        }

        [Fact]
        public async Task NewLoginOnCharacterCreationReplacesTheOldOne()
        {
            var (first, _, email) = await this.AtCharacterCreationAsync();
            await using (first)
            {
                // The same account logs in again from another client
                var login = await this.Servers.LoginAsync(email, Password);
                Assert.True(login.Accepted);
                await using var second = await this.Servers.ConnectGameAsync();
                Assert.Equal(NewRole, (await second.LoginAsync(login.Token)).Message);

                // The first client is closed, and the new one creates the character
                Assert.True(await first.WaitClosedAsync());
                var name = this.Servers.NewCharacterName();
                Assert.Equal(AnswerOk, await second.CreateCharacterAsync(name, 1003, 10, (uint)login.Token));
                var result = await second.LoginAsync(login.Token);
                Assert.True(result.InGame);
                Assert.Equal(name, result.User.Name);
            }
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Diagnostics;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    /// <summary>
    /// When the servers close a connection: a new connection that never starts talking is
    /// dropped after the handshake timeout, but players are never dropped for being idle,
    /// and a newer login for the same account takes over at every step.
    /// </summary>
    public class ConnectionLifetimeTests : IntegrationTest
    {
        public ConnectionLifetimeTests(ServerFixture servers) : base(servers) { }

        // Longer than the handshake timeout, so an idle limit would have closed the client
        private static readonly TimeSpan Idle =
            TimeSpan.FromSeconds(ServerFixture.HandshakeTimeoutSeconds * 2 + 1);

        /// <summary>Opens a connection, sends nothing, and times how long it stays open.</summary>
        private static async Task<TimeSpan> SilentConnectionLifetimeAsync(int port)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(ServerFixture.Host, port);
            var watch = Stopwatch.StartNew();
            var stream = tcp.GetStream();
            var buffer = new byte[4096];
            var deadline = Task.Delay(TimeSpan.FromSeconds(15));
            try
            {
                while (true)
                {
                    var read = stream.ReadAsync(buffer).AsTask();
                    if (await Task.WhenAny(read, deadline) == deadline)
                        throw new Xunit.Sdk.XunitException("The server kept a silent connection open");
                    if (await read == 0)
                        break;
                }
            }
            catch (System.IO.IOException) { }
            return watch.Elapsed;
        }

        [Fact]
        public async Task SilentGameConnectionIsDroppedAfterTheHandshakeTimeout()
        {
            var lifetime = await SilentConnectionLifetimeAsync(this.Servers.GamePort);
            Assert.True(lifetime >= TimeSpan.FromSeconds(ServerFixture.HandshakeTimeoutSeconds - 0.5),
                $"Dropped after {lifetime}, before the handshake timeout");
        }

        [Fact]
        public async Task SilentAccountConnectionIsDroppedAfterTheHandshakeTimeout()
        {
            var lifetime = await SilentConnectionLifetimeAsync(this.Servers.AccountPort);
            Assert.True(lifetime >= TimeSpan.FromSeconds(ServerFixture.HandshakeTimeoutSeconds - 0.5),
                $"Dropped after {lifetime}, before the handshake timeout");
        }

        [Fact]
        public async Task IdlePlayerOnCharacterCreationStaysConnected()
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
            {
                await Task.Delay(Idle);
                Assert.False(game.Closed);
                var name = this.Servers.NewCharacterName();
                Assert.Equal(AnswerOk, await game.CreateCharacterAsync(name, 1003, 10, (uint)token));
            }
        }

        [Fact]
        public async Task IdlePlayerInTheGameStaysConnected()
        {
            var (player, _) = await this.PlayingAsync();
            await using (player)
            {
                await Task.Delay(Idle);
                Assert.False(player.Game.Closed);
                var spawn = await player.Game.EnterMapAsync(player.User.Id);
                Assert.Equal(1010u, spawn.Command);
            }
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

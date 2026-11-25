namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Shared.Models.Admin;
    using StreamJsonRpc;
    using Xunit;

    public class PkRpcTests : IntegrationTest
    {
        private const uint Actor = 4244;

        public PkRpcTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task SnapshotAndReleaseHandleOnlineAndOfflineInmates()
        {
            await using var player = (await this.PlayingAtAsync(1002, 300, 340)).Player;
            var tcpAdmin = new TcpClient();
            await tcpAdmin.ConnectAsync(ServerFixture.Host, this.Servers.RpcPort);
            var tcpPk = new TcpClient();
            await tcpPk.ConnectAsync(ServerFixture.Host, this.Servers.RpcPort);
            using (tcpAdmin)
            using (tcpPk)
            using (var admin = (IDisposable)JsonRpc.Attach<IGameAdmin>(tcpAdmin.GetStream()))
            using (var pk = (IDisposable)JsonRpc.Attach<IGamePk>(tcpPk.GetStream()))
            {
                var adminRpc = (IGameAdmin)admin;
                var pkRpc = (IGamePk)pk;

                // Not in jail yet
                var notJailed = await pkRpc.ReleaseFromJail(new CharacterArgs { ActorAccountID = Actor, CharacterID = player.User.Id });
                Assert.False(notJailed.Ok);
                Assert.Equal(AdminErrors.Invalid, notJailed.Error);

                // Missing character
                var missing = await pkRpc.ReleaseFromJail(new CharacterArgs { ActorAccountID = Actor, CharacterID = 999888777 });
                Assert.False(missing.Ok);
                Assert.Equal(AdminErrors.NotFound, missing.Error);

                // Jail online player -> appears in PkSnapshot().Jailed
                var jailed = await adminRpc.Jail(new CharacterArgs { ActorAccountID = Actor, CharacterID = player.User.Id });
                Assert.True(jailed.Ok, jailed.Error);

                var snap = await pkRpc.PkSnapshot();
                var inmate = Assert.Single(snap.Jailed, j => j.CharacterID == player.User.Id);
                Assert.Equal(player.User.Name, inmate.Name);

                // Release online player -> removed from Jailed and moved back to Twin City (1002)
                var released = await pkRpc.ReleaseFromJail(new CharacterArgs { ActorAccountID = Actor, CharacterID = player.User.Id });
                Assert.True(released.Ok, released.Error);
                Assert.True(released.Online);

                var after = await pkRpc.PkSnapshot();
                Assert.DoesNotContain(after.Jailed, j => j.CharacterID == player.User.Id);
                var character = await adminRpc.Character(player.User.Id);
                Assert.Equal(1002u, character.MapID);

                // Offline release: jail, log out, then release
                Assert.True((await adminRpc.Jail(new CharacterArgs { ActorAccountID = Actor, CharacterID = player.User.Id })).Ok);
                await this.LogOutAsync(player);

                var offlineRelease = await pkRpc.ReleaseFromJail(new CharacterArgs { ActorAccountID = Actor, CharacterID = player.User.Id });
                Assert.True(offlineRelease.Ok, offlineRelease.Error);
                Assert.False(offlineRelease.Online);

                var savedMap = Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                    "SELECT MapID FROM `character` WHERE CharacterID = @id", ("@id", player.User.Id)));
                Assert.Equal(1002u, savedMap);
            }
        }
    }
}

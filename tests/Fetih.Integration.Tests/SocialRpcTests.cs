namespace Fetih.Integration.Tests
{
    using System;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Fetih.Shared.Models.Admin;
    using StreamJsonRpc;
    using Xunit;

    public class SocialRpcTests : IntegrationTest
    {
        private const uint Actor = 4243;

        public SocialRpcTests(ServerFixture servers) : base(servers) { }

        private async Task<(TcpClient Tcp, IGameSocial Social)> ConnectAsync()
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(ServerFixture.Host, this.Servers.RpcPort);
            return (tcp, JsonRpc.Attach<IGameSocial>(tcp.GetStream()));
        }

        [Fact]
        public async Task GuildWarStartsAndEndsByHand()
        {
            var (tcp, social) = await ConnectAsync();
            using (tcp)
            using ((IDisposable)social)
            {
                var before = await social.GuildWarStatus();
                Assert.False(string.IsNullOrEmpty(before.Schedule));
                if (before.Active)
                    Assert.True((await social.EndGuildWar(new AdminArgs { ActorAccountID = Actor })).Ok);

                Assert.True((await social.StartGuildWar(new AdminArgs { ActorAccountID = Actor })).Ok);
                var running = await social.GuildWarStatus();
                Assert.True(running.Active);
                Assert.Equal(running.PoleMaxLife, running.PoleLife);
                Assert.False((await social.StartGuildWar(new AdminArgs { ActorAccountID = Actor })).Ok);

                Assert.True((await social.EndGuildWar(new AdminArgs { ActorAccountID = Actor })).Ok);
                Assert.False((await social.GuildWarStatus()).Active);
                Assert.False((await social.EndGuildWar(new AdminArgs { ActorAccountID = Actor })).Ok);
            }
        }

        [Fact]
        public async Task MissingGuildsAndPostsAreReported()
        {
            var (tcp, social) = await ConnectAsync();
            using (tcp)
            using ((IDisposable)social)
            {
                var guild = await social.DisbandGuild(new GuildArgs { ActorAccountID = Actor, GuildID = 987654 });
                Assert.Equal(AdminErrors.NotFound, guild.Error);
                var post = await social.DeleteBoardPost(new BoardPostArgs { ActorAccountID = Actor, Channel = 2201, CharacterID = 987654 });
                Assert.Equal(AdminErrors.NotFound, post.Error);
                var notBoard = await social.DeleteBoardPost(new BoardPostArgs { ActorAccountID = Actor, Channel = 2000, CharacterID = 1 });
                Assert.Equal(AdminErrors.Invalid, notBoard.Error);
            }
        }
    }
}

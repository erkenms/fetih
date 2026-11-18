namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    [Collection(ServerCollection.Name)]
    public class SmokeTests
    {
        private readonly ServerFixture Servers;
        public SmokeTests(ServerFixture servers) => this.Servers = servers;

        [Fact]
        public async Task NewPlayerReachesTheMap()
        {
            var email = await this.Servers.CreateAccountAsync();
            var name = this.Servers.NewCharacterName();
            await using var player = await this.Servers.PlayAsync(
                email, "secret1", new NewCharacter(name, 1003, 10));
            Assert.Equal(name, player.User.Name);
            Assert.Equal(1010u, player.Spawn.Command);
        }
    }
}

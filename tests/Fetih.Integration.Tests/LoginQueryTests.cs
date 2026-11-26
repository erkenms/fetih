namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Xunit;

    public class LoginQueryTests : IntegrationTest
    {
        public LoginQueryTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task TheClientsLoginQueriesAreAcceptedQuietly()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 560)).Player;
            // The task status query, the own flower rank query and the anti-cheat count ask,
            // as the 5619 client sends them.
            await player.Game.SendAsync(Convert.FromHexString(
                "2000" + "6E04" + "0300" + "0200" + "90010000" + "0000000000000000"
                + "91010000" + "0000000000000000"));
            await player.Game.SendAsync(Convert.FromHexString("50007F0402" + new string('0', 150)));
            await player.Game.SendAsync(Convert.FromHexString(
                "24000904" + "04000000" + "EE030000" + new string('0', 48)));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForTalkAsync(t => t.Text.StartsWith("Missing packet"), Already));
        }
    }
}

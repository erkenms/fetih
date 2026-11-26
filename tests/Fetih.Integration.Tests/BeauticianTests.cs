namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    // Twin City's Beautician (10020) at 459,332; new characters have 1,000 silver and a male
    // face below 50
    public class BeauticianTests : IntegrationTest
    {
        public BeauticianTests(ServerFixture servers) : base(servers) { }

        // The reason the face was refused, from what was logged after it was chosen
        private async Task<string> RefusedAsync(Player player, uint face)
        {
            int mark = this.Servers.Mark();
            await ChooseAsync(player, face);
            var refused = await this.Servers.WaitForEventAsync(
                "game", "character.avatar_rejected", "name", player.User.Name, after: mark);
            return refused.GetProperty("reason").GetString();
        }

        private static Task ChooseAsync(Player player, uint face) =>
            player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.ChangeAvatar, command: face));

        [Fact]
        public async Task EachFaceCostsFiveHundredSilverNearTheBeautician()
        {
            await using var player = (await this.PlayingAtAsync(1002, 459, 336)).Player;

            Assert.Equal("face_not_offered", await this.RefusedAsync(player, 201));

            await ChooseAsync(player, 50);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Mesh, out var m) && m == 50 * 10000 + 1003));
            Assert.NotNull(await SilverAsync(player, 500));

            await ChooseAsync(player, 51);
            Assert.NotNull(await SilverAsync(player, 0));
            Assert.Equal("no_silver", await this.RefusedAsync(player, 52));
        }

        [Fact]
        public async Task FacesAreOnlyChangedAtTheBeautician()
        {
            await using var player = (await this.PlayingAtAsync(1002, 459, 300)).Player;
            Assert.Equal("no_beautician", await this.RefusedAsync(player, 50));
        }
    }
}

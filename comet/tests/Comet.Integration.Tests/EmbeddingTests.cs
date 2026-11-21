namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class EmbeddingTests : IntegrationTest
    {
        public EmbeddingTests(ServerFixture servers) : base(servers) { }

        // A game master with a two-socket helmet
        private async Task<(Player Player, ItemInfo Helmet)> SocketedHelmetAsync(int x)
        {
            var (first, email) = await this.PlayingAtAsync(1002, x, 900, GameMaster);
            var helmet = await GiveAsync(first, ServerFixture.Helmet.Id);
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE item SET Gem1 = 255, Gem2 = 255 WHERE ItemID = @id", ("@id", helmet.Id));
            return (await this.Servers.PlayAsync(email, Password), helmet);
        }

        [Fact]
        public async Task GemsGoIntoSocketsAndRemovingTheFirstMovesTheSecondUp()
        {
            var (player, helmet) = await SocketedHelmetAsync(600);
            await using var _ = player;
            uint id = player.User.Id;
            var dragon = await GiveAsync(player, ServerFixture.SuperDragonGem.Id);
            var tortoise = await GiveAsync(player, ServerFixture.NormalTortoiseGem.Id);

            await player.Game.EmbedAsync(id, helmet.Id, dragon.Id, 1);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, dragon.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Gem1 == 13 && i.Gem2 == 255));
            Assert.NotNull(await player.Game.WaitForGemEmbedAsync(m => m.GemId == dragon.Id));

            await player.Game.EmbedAsync(id, helmet.Id, tortoise.Id, 2);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Gem1 == 13 && i.Gem2 == 71));

            await player.Game.RemoveGemAsync(id, helmet.Id, 1);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.Gem1 == 71 && i.Gem2 == 255));
            var embedded = await this.Servers.WaitForEventAsync("game", "item.embedded", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("action").GetString() == "Remove");
            Assert.Equal(13, embedded.GetProperty("gem").GetInt32());

            var sockets = await this.Servers.QueryGameScalarAsync(
                "SELECT Gem1 * 1000 + Gem2 FROM item WHERE ItemID = @id", ("@id", helmet.Id));
            Assert.Equal(71255L, System.Convert.ToInt64(sockets));
        }

        [Fact]
        public async Task GemsDontGoWhereThereIsNoSocketOrNotForTheirItem()
        {
            await using var player = (await this.PlayingAtAsync(1002, 640, 900, GameMaster)).Player;
            uint id = player.User.Id;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var dragon = await GiveAsync(player, ServerFixture.SuperDragonGem.Id);
            await player.Game.EmbedAsync(id, helmet.Id, dragon.Id, 1);
            var refused = await this.Servers.WaitForEventAsync("game", "item.embed_rejected", "name", player.User.Name);
            Assert.Equal("no_socket", refused.GetProperty("reason").GetString());

            var thunder = await GiveAsync(player, ServerFixture.SuperThunderGem.Id);
            await player.Game.EmbedAsync(id, helmet.Id, thunder.Id, 1);
            refused = await this.Servers.WaitForEventAsync("game", "item.embed_rejected", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("reason").GetString() != "no_socket");
            Assert.Equal("wrong_item", refused.GetProperty("reason").GetString());
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", id));
            Assert.Equal(3L, (long)count);
        }
    }
}

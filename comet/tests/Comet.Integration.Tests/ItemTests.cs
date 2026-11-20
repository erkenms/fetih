namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class ItemTests : IntegrationTest
    {
        private const ushort GameMaster = 3;

        public ItemTests(ServerFixture servers) : base(servers) { }

        private static Task CommandAsync(Player player, string command) =>
            player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", command);

        [Fact]
        public async Task GivenItemsAreSentAndKeptAfterLogout()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 620, 540, GameMaster);
            Assert.Empty(player.Inventory);

            await CommandAsync(player, "/item " + ServerFixture.Blade.Id);
            var item = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            Assert.NotNull(item);
            Assert.Equal((ServerFixture.Blade.Amount, ServerFixture.Blade.Amount, (ushort)1, (ushort)0),
                (item.Amount, item.AmountLimit, item.Action, item.Position));

            var logged = await this.Servers.WaitForEventAsync("game", "item.created", "name", player.User.Name);
            Assert.Equal((item.Id, ServerFixture.Blade.Id, "gm"), (logged.GetProperty("itemId").GetUInt32(),
                logged.GetProperty("typeId").GetUInt32(), logged.GetProperty("reason").GetString()));

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            await using var again = await this.Servers.PlayAsync(email, Password);
            var kept = Assert.Single(again.Inventory);
            Assert.Equal((item.Id, ServerFixture.Blade.Id), (kept.Id, kept.TypeId));
        }

        [Fact]
        public async Task TheInventoryHoldsFortyItems()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 640, 540, GameMaster);
            await using var _ = player;
            await CommandAsync(player, $"/item {ServerFixture.Potion.Id} 39");
            await CommandAsync(player, $"/item {ServerFixture.Potion.Id} 2");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("inventory is full")));

            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(40L, (long)count);
        }

        [Fact]
        public async Task UnknownTypesAreRefused()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 540, GameMaster)).Player;
            await CommandAsync(player, "/item 999");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("no item type 999")));
        }

        [Fact]
        public async Task TeleportScrollsGoToTheirCityAndAreUsedUp()
        {
            await using var player = (await this.PlayingAtAsync(1006, 30, 30, GameMaster)).Player;
            await CommandAsync(player, "/item " + ServerFixture.TwinCityGate.Id);
            var scroll = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.TwinCityGate.Id);
            Assert.NotNull(scroll);

            await player.Game.UseAsync(scroll.Id);
            var moved = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((1002u, (ushort)430, (ushort)380), (moved?.Command, moved?.X, moved?.Y));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, scroll.Id));
            var used = await this.Servers.WaitForEventAsync("game", "item.used", "name", player.User.Name);
            Assert.Equal(scroll.Id, used.GetProperty("itemId").GetUInt32());
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(0L, (long)count);
        }

        [Fact]
        public async Task TeleportScrollsDontWorkInJail()
        {
            await using var player = (await this.PlayingAtAsync(6000, 30, 30, GameMaster)).Player;
            await CommandAsync(player, "/item " + ServerFixture.TwinCityGate.Id);
            var scroll = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.TwinCityGate.Id);

            await player.Game.UseAsync(scroll.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name);
            Assert.Equal("teleport_disabled", refused.GetProperty("reason").GetString());
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("can't teleport")));
        }
    }
}

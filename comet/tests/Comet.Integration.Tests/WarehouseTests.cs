namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Twin City's warehouseman (NPC 8, role 3) at 409,351
    public class WarehouseTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint Warehouseman = 8, Blacksmith = ServerFixture.Blacksmith;

        public WarehouseTests(ServerFixture servers) : base(servers) { }

        private static Task<UserAttribMessage> SilverAsync(Player player, ulong silver) =>
            player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == silver);

        [Fact]
        public async Task TalkingToTheWarehousemanOpensTheWarehouse()
        {
            await using var player = (await this.PlayingAtAsync(1002, 410, 355)).Player;
            await player.Game.TalkToNpcAsync(Warehouseman);
            var open = await player.Game.WaitForActionAsync(ActionMessage.OpenWindow, player.User.Id);
            Assert.Equal((ActionMessage.WarehouseWindow, (ushort)410, (ushort)355),
                (open?.Command, open?.Argument1, open?.Argument2));
        }

        [Fact]
        public async Task StoredItemsStayInTheWarehouseAfterLogout()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 410, 356, GameMaster);
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + ServerFixture.Blade.Id);
            var blade = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            Assert.NotNull(blade);

            await player.Game.SendPackageAsync(Warehouseman, PackageMessage.Deposit, blade.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, blade.Id));
            var shown = await player.Game.WaitForPackageAsync(p => p.Items.Any(i => i.Id == blade.Id));
            Assert.Equal((Warehouseman, PackageMessage.Query), (shown?.NpcId, shown?.Action));
            await this.Servers.WaitForEventAsync("game", "item.stored", "name", player.User.Name);

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.DoesNotContain(again.Inventory, i => i.Id == blade.Id);

            await again.Game.SendPackageAsync(Warehouseman, PackageMessage.Query);
            var kept = await again.Game.WaitForPackageAsync(p => p.Items.Any(i => i.Id == blade.Id));
            Assert.Equal(ServerFixture.Blade.Id, kept?.Items.Single(i => i.Id == blade.Id).TypeId);

            await again.Game.SendPackageAsync(Warehouseman, PackageMessage.Withdraw, blade.Id);
            var taken = await again.Game.WaitForPackageAsync(p => p.Action == PackageMessage.Withdraw);
            Assert.Equal(blade.Id, taken?.ItemId);
            Assert.NotNull(await again.Game.WaitForItemAsync(i => i.Id == blade.Id));
            var position = await this.Servers.QueryGameScalarAsync(
                "SELECT Position FROM item WHERE ItemID = @id", ("@id", blade.Id));
            Assert.Equal(0, System.Convert.ToInt32(position));
        }

        [Fact]
        public async Task OnlyWarehousemenKeepItems()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 341, GameMaster)).Player;
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + ServerFixture.Blade.Id);
            var blade = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            await player.Game.SendPackageAsync(Blacksmith, PackageMessage.Deposit, blade.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.warehouse_rejected", "name", player.User.Name);
            Assert.Equal("not_a_warehouse", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task SilverCanBeStoredAndTakenOut()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 410, 357);
            await player.Game.StoreSilverAsync(Warehouseman, 300);
            Assert.NotNull(await SilverAsync(player, 700));
            Assert.Equal(300u, await player.Game.AskStoredSilverAsync(Warehouseman));

            await player.Game.TakeSilverAsync(Warehouseman, 400);
            var refused = await this.Servers.WaitForEventAsync("game", "item.warehouse_rejected", "name", player.User.Name);
            Assert.Equal(-400, refused.GetProperty("amount").GetInt64());

            await player.Game.TakeSilverAsync(Warehouseman, 100);
            Assert.NotNull(await SilverAsync(player, 800));

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(200u, await again.Game.AskStoredSilverAsync(Warehouseman));
        }
    }
}

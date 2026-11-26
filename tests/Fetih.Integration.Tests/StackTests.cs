namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    // The test Stancher stacks to ServerFixture.PotionStack
    public class StackTests : IntegrationTest
    {
        private const uint Pharmacist = ServerFixture.Pharmacist;
        private static readonly uint Potion = ServerFixture.Potion.Id;

        public StackTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task BoughtPotionsFillAStackBeforeTakingASlot()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 341)).Player;
            await player.Game.BuyAsync(Pharmacist, Potion, 7);
            Assert.NotNull(await SilverAsync(player, 1000 - 7 * ItemFiles.TestPrice));
            var full = await player.Game.WaitForItemAsync(i => i.TypeId == Potion && i.StackAmount == ServerFixture.PotionStack);
            Assert.NotNull(full);
            var second = await player.Game.WaitForItemAsync(i => i.TypeId == Potion && i.Id != full.Id && i.StackAmount == 2);
            Assert.NotNull(second);
            Assert.Equal("5,2", await this.StacksAsync(player));
        }

        [Fact]
        public async Task SplittingMovesPartOfAStackToANewSlot()
        {
            await using var player = (await this.PlayingAtAsync(1002, 760, 540, GameMaster)).Player;
            await CommandAsync(player, $"/item {Potion} 4");
            var stack = await player.Game.WaitForItemAsync(i => i.TypeId == Potion && i.StackAmount == 4);
            Assert.NotNull(stack);

            await player.Game.SplitAsync(stack.Id, 3);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == stack.Id && i.StackAmount == 1));
            var part = await player.Game.WaitForItemAsync(i => i.Id != stack.Id && i.TypeId == Potion);
            Assert.Equal((ushort)3, part?.StackAmount);
            var split = await this.Servers.WaitForEventAsync("game", "item.split", "name", player.User.Name);
            Assert.Equal(part.Id, split.GetProperty("newItemId").GetUInt32());
            Assert.Equal("1,3", await this.StacksAsync(player));

            // The whole stack, or a single item, can't be split
            await player.Game.SplitAsync(part.Id, 3);
            var refused = await this.Servers.WaitForEventAsync("game", "item.split_rejected", "name", player.User.Name);
            Assert.Equal("bad_amount", refused.GetProperty("reason").GetString());
            await player.Game.SplitAsync(stack.Id, 1);
            refused = await this.Servers.WaitForEventAsync("game", "item.split_rejected", "name", player.User.Name);
            Assert.Equal("bad_amount", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task UsingAPotionTakesOneOffTheStack()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 780, 540, GameMaster);
            await CommandAsync(first, $"/item {Potion} 2");
            Assert.NotNull(await first.Game.WaitForItemAsync(i => i.TypeId == Potion && i.StackAmount == 2));
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET HealthPoints = 10 WHERE Name = @name", ("@name", first.User.Name));
            await using var player = await this.Servers.PlayAsync(email, Password);
            var stack = Assert.Single(player.Inventory);
            Assert.Equal((ushort)2, stack.StackAmount);

            await player.Game.UseAsync(stack.Id);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == stack.Id && i.StackAmount == 1));
            Assert.Equal("1", await this.StacksAsync(player));
            await player.Game.UseAsync(stack.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, stack.Id));
        }

        [Fact]
        public async Task APickedUpItemJoinsAStackWithRoom()
        {
            await using var player = (await this.PlayingAtAsync(1002, 680, 580, GameMaster)).Player;
            await CommandAsync(player, $"/item {Potion} 2");
            var stack = await player.Game.WaitForItemAsync(i => i.TypeId == Potion && i.StackAmount == 2);
            Assert.NotNull(stack);
            await player.Game.SplitAsync(stack.Id, 1);
            var part = await player.Game.WaitForItemAsync(i => i.Id != stack.Id && i.TypeId == Potion);
            Assert.NotNull(part);

            await player.Game.DropAsync(part.Id);
            var shown = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create);
            Assert.NotNull(shown);
            await player.Game.PickUpAsync(shown.Id, shown.X, shown.Y);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == stack.Id && i.StackAmount == 2));
            Assert.Equal("2", await this.StacksAsync(player));
        }

        [Fact]
        public async Task SellingAStackPaysForEveryItemInIt()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 342)).Player;
            await player.Game.BuyAsync(Pharmacist, Potion, 3);
            var stack = await player.Game.WaitForItemAsync(i => i.TypeId == Potion && i.StackAmount == 3);
            Assert.NotNull(stack);
            Assert.NotNull(await SilverAsync(player, 1000 - 3 * ItemFiles.TestPrice));

            await player.Game.SellAsync(Pharmacist, stack.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, stack.Id));
            Assert.NotNull(await SilverAsync(player, 1000 - 3 * ItemFiles.TestPrice + 3 * (ItemFiles.TestPrice / 3)));
        }

        private async Task<string> StacksAsync(Player player)
        {
            await SyncAsync(player);
            var stacks = await this.Servers.QueryGameScalarAsync(
                "SELECT GROUP_CONCAT(StackAmount ORDER BY ItemID) FROM item WHERE CharacterID = @id",
                ("@id", player.User.Id));
            return stacks?.ToString();
        }
    }
}

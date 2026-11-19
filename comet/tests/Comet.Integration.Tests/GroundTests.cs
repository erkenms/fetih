namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class GroundTests : IntegrationTest
    {
        private const ushort GameMaster = 3;

        public GroundTests(ServerFixture servers) : base(servers) { }

        private static async Task<ItemInfo> GiveAsync(Player player, uint type)
        {
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + type);
            var item = await player.Game.WaitForItemAsync(i => i.TypeId == type);
            Assert.NotNull(item);
            return item;
        }

        private Task<object> ItemColumnAsync(string column, uint itemId) =>
            this.Servers.QueryGameScalarAsync($"SELECT {column} FROM item WHERE ItemID = @id", ("@id", itemId));

        [Fact]
        public async Task ADroppedItemCanBePickedUpByAnotherPlayer()
        {
            await using var player = (await this.PlayingAtAsync(1002, 620, 580, GameMaster)).Player;
            await using var other = (await this.PlayingAtAsync(1002, 621, 580)).Player;
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);

            await player.Game.DropAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, blade.Id));
            var shown = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create);
            Assert.Equal((ServerFixture.Blade.Id, (ushort)620, (ushort)580), (shown?.TypeId, shown?.X, shown?.Y));
            var seen = await other.Game.WaitForGroundItemAsync(g => g.Id == shown.Id && g.Action == GroundItemMessage.Create);
            Assert.NotNull(seen);
            Assert.Equal((254, 0), (Convert.ToInt32(await this.ItemColumnAsync("Position", blade.Id)),
                Convert.ToInt32(await this.ItemColumnAsync("CharacterID", blade.Id))));
            var dropped = await this.Servers.WaitForEventAsync("game", "item.dropped", "name", player.User.Name);
            Assert.Equal(blade.Id, dropped.GetProperty("itemId").GetUInt32());

            await other.Game.PickUpAsync(shown.Id, shown.X, shown.Y);
            var got = await other.Game.WaitForItemAsync(i => i.Id == blade.Id);
            Assert.NotNull(got);
            Assert.NotNull(await player.Game.WaitForGroundItemAsync(g => g.Id == shown.Id && g.Action == GroundItemMessage.Delete));
            Assert.Equal((0, (int)other.User.Id), (Convert.ToInt32(await this.ItemColumnAsync("Position", blade.Id)),
                Convert.ToInt32(await this.ItemColumnAsync("CharacterID", blade.Id))));
            var picked = await this.Servers.WaitForEventAsync("game", "item.picked_up", "name", other.User.Name);
            Assert.Equal(shown.Id, picked.GetProperty("groundId").GetUInt32());
        }

        [Fact]
        public async Task DroppedSilverIsAPileThatPaysBack()
        {
            await using var player = (await this.PlayingAtAsync(1002, 640, 580)).Player;
            await player.Game.DropSilverAsync(100);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 900));
            var pile = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create);
            Assert.Equal(1090010u, pile?.TypeId);

            await player.Game.DropSilverAsync(5000);
            var refused = await this.Servers.WaitForEventAsync("game", "item.ground_rejected", "name", player.User.Name);
            Assert.Equal("not_enough_silver", refused.GetProperty("reason").GetString());

            await player.Game.PickUpAsync(pile.Id, pile.X, pile.Y);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1000));
        }

        [Fact]
        public async Task EachCellHoldsOneItemAndFarItemsCantBePickedUp()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 580)).Player;
            await player.Game.DropSilverAsync(10);
            var first = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create);
            await player.Game.DropSilverAsync(10);
            var second = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create && g.Id != first.Id);
            Assert.Equal((660, 580), (first.X, first.Y));
            Assert.NotEqual((first.X, first.Y), (second.X, second.Y));
            Assert.True(Math.Max(Math.Abs(second.X - 660), Math.Abs(second.Y - 580)) == 1);

            await player.Game.JumpAsync(player.User.Id, 660, 580, 664, 580);
            await player.Game.PickUpAsync(first.Id, first.X, first.Y);
            var refused = await this.Servers.WaitForEventAsync("game", "item.ground_rejected",
                e => e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("reason").GetString() == "too_far");
            Assert.Equal(first.Id, refused.GetProperty("id").GetUInt32());
        }

        [Fact]
        public async Task GroundItemsExpireAndAreDeleted()
        {
            await using var player = (await this.PlayingAtAsync(1002, 680, 580, GameMaster)).Player;
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.DropAsync(blade.Id);
            var shown = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create);
            Assert.NotNull(shown);

            var gone = await player.Game.WaitForGroundItemAsync(g => g.Id == shown.Id && g.Action == GroundItemMessage.Delete,
                TimeSpan.FromSeconds(ServerFixture.GroundExpireSeconds + 5));
            Assert.NotNull(gone);
            await this.Servers.WaitForEventAsync("game", "item.expired", "groundId", shown.Id.ToString());
            Assert.Null(await this.ItemColumnAsync("ItemID", blade.Id));
        }

        [Fact]
        public async Task PlayersArrivingSeeWhatLiesAround()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 580)).Player;
            await player.Game.DropSilverAsync(30);
            var pile = await player.Game.WaitForGroundItemAsync(g => g.Action == GroundItemMessage.Create);
            await using var newcomer = (await this.PlayingAtAsync(1002, 705, 580)).Player;
            Assert.NotNull(await newcomer.Game.WaitForGroundItemAsync(g => g.Id == pile.Id && g.Action == GroundItemMessage.Create));
        }
    }
}

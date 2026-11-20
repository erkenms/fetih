namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Booth flags of the market in Data/npcs.json: each test takes its own, in the column at
    // X 118. A booth opens three cells east of its flag, with the flag's ID plus 100000.
    public class BoothTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint Market = ServerFixture.Market;
        private const ushort FlagRole = 16, BoothLook = 406, BoothRole = 14;
        private const uint BoothIds = 100000;
        private const uint Sit = 250;

        public BoothTests(ServerFixture servers) : base(servers) { }

        private static Task<UserAttribMessage> SilverAsync(Player player, ulong silver) =>
            player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == silver);

        private Task<System.Text.Json.JsonElement> EventAsync(string name, string player, string reason) =>
            this.Servers.WaitForEventAsync("game", name, e =>
                e.GetProperty("name").GetString() == player && e.GetProperty("reason").GetString() == reason);

        [Fact]
        public async Task ABoothOpensBesideItsFlagAndSellsItems()
        {
            const uint flag = 50, id = flag + BoothIds; // at 118,217
            await using var seller = (await this.PlayingAtAsync(Market, 120, 217, GameMaster)).Player;
            await using var buyer = (await this.PlayingAtAsync(Market, 122, 220)).Player;
            Assert.NotNull(await buyer.Game.WaitForNpcAsync(n => n.Id == flag && n.Role == FlagRole));
            await seller.Game.SayAsync(TalkChannels.Talk, seller.User.Name, "ALLUSERS", "/item " + ServerFixture.Blade.Id);
            var blade = await seller.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);

            Assert.Equal(id, await seller.Game.OpenBoothAsync(seller.User.Id, 121, 217, 0));
            var booth = await buyer.Game.WaitForNpcAsync(n => n.Id == id);
            Assert.False(await buyer.Game.WaitForRemovalAsync(flag, TimeSpan.FromMilliseconds(300)));
            Assert.NotNull(booth);
            Assert.Equal(((ushort)121, (ushort)217, BoothLook, seller.User.Name), (booth.X, booth.Y, booth.Lookface, booth.Name));
            Assert.Equal(BoothRole, booth.Role);

            await seller.Game.AddToBoothAsync(blade.Id, 300);
            Assert.NotNull(await seller.Game.WaitForItemActionAsync(ItemMessage.BoothAdd, blade.Id));
            await seller.Game.SayAsync(TalkChannels.Vendor, seller.User.Name, "ALLUSERS", "Blades, cheap");
            Assert.NotNull(await buyer.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Vendor && t.Text == "Blades, cheap"));

            await buyer.Game.ViewBoothAsync(id);
            var shown = await buyer.Game.WaitForBoothItemAsync(i => i.Id == blade.Id);
            Assert.Equal((id, 300u, ServerFixture.Blade.Id, BoothItem.Silver),
                (shown?.BoothId, shown?.Price, shown?.TypeId, shown?.View));

            await buyer.Game.BuyFromBoothAsync(blade.Id, id);
            Assert.NotNull(await buyer.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 1));
            Assert.NotNull(await SilverAsync(buyer, 700));
            Assert.NotNull(await seller.Game.WaitForItemActionAsync(ItemMessage.BoothRemove, blade.Id));
            Assert.NotNull(await seller.Game.WaitForItemActionAsync(ItemMessage.Remove, blade.Id));
            Assert.NotNull(await SilverAsync(seller, 1300));
            Assert.Equal((int)buyer.User.Id, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT CharacterID FROM item WHERE ItemID = @id", ("@id", blade.Id))));

            var sold = await this.Servers.WaitForEventAsync("game", "booth.sold", "name", buyer.User.Name);
            Assert.Equal(seller.User.Name, sold.GetProperty("owner").GetString());
            Assert.Equal(300u, sold.GetProperty("price").GetUInt32());

            // Sold out: nothing left to show
            await buyer.Game.ViewBoothAsync(id);
            Assert.Null(await buyer.Game.WaitForBoothItemAsync(_ => true, TimeSpan.FromMilliseconds(300)));
        }

        [Fact]
        public async Task ClosingStandingUpOrMovingRemovesTheBooth()
        {
            const uint flag = 51, id = flag + BoothIds; // at 118,213
            await using var seller = (await this.PlayingAtAsync(Market, 120, 213)).Player;
            Assert.Equal(id, await seller.Game.OpenBoothAsync(seller.User.Id, 121, 213, 0));
            Assert.NotNull(await seller.Game.WaitForNpcAsync(n => n.Id == id && n.Role == BoothRole));

            await seller.Game.CloseBoothAsync(seller.User.Id);
            Assert.True(await seller.Game.WaitForRemovalAsync(id));
            await EventAsync("booth.closed", seller.User.Name, "closed");

            Assert.Equal(id, await seller.Game.OpenBoothAsync(seller.User.Id, 121, 213, 0));
            await seller.Game.EmoteAsync(seller.User.Id, Sit, 6);
            Assert.False(await seller.Game.WaitForRemovalAsync(id, TimeSpan.FromMilliseconds(300)));
            await seller.Game.EmoteAsync(seller.User.Id, 0, 6);
            Assert.True(await seller.Game.WaitForRemovalAsync(id));
            await EventAsync("booth.closed", seller.User.Name, "stood_up");

            Assert.Equal(id, await seller.Game.OpenBoothAsync(seller.User.Id, 121, 213, 0));
            await seller.Game.SayAsync(TalkChannels.Talk, seller.User.Name, "ALLUSERS", "/closebooth");
            Assert.True(await seller.Game.WaitForRemovalAsync(id));
            var stay = await seller.Game.WaitForActionAsync(ActionMessage.Teleport, seller.User.Id);
            Assert.Equal((Market, (ushort)120, (ushort)213), (stay?.Command, stay?.X, stay?.Y));
            await EventAsync("booth.closed", seller.User.Name, "command");

            Assert.Equal(id, await seller.Game.OpenBoothAsync(seller.User.Id, 121, 213, 0));
            await seller.Game.WalkAsync(seller.User.Id, 6);
            Assert.True(await seller.Game.WaitForRemovalAsync(id));
            await EventAsync("booth.closed", seller.User.Name, "moved");
        }

        [Fact]
        public async Task BoothsNeedAFlagAndBuyersTheMoney()
        {
            const uint flag = 52, id = flag + BoothIds; // at 118,209
            await using var stranger = (await this.PlayingAtAsync(Market, 200, 100)).Player;
            Assert.Null(await stranger.Game.OpenBoothAsync(stranger.User.Id, 200, 100, 0, TimeSpan.FromMilliseconds(300)));
            await EventAsync("booth.rejected", stranger.User.Name, "no_free_flag");

            var seller = (await this.PlayingAtAsync(Market, 120, 209, GameMaster)).Player;
            await using var buyer = (await this.PlayingAtAsync(Market, 122, 207)).Player;
            await seller.Game.SayAsync(TalkChannels.Talk, seller.User.Name, "ALLUSERS", "/item " + ServerFixture.Blade.Id);
            var blade = await seller.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            Assert.Equal(id, await seller.Game.OpenBoothAsync(seller.User.Id, 121, 209, 0));

            await seller.Game.AddToBoothAsync(12345, 10);
            await EventAsync("booth.rejected", seller.User.Name, "not_in_inventory");
            await seller.Game.AddToBoothAsync(blade.Id, 5000);
            Assert.NotNull(await seller.Game.WaitForItemActionAsync(ItemMessage.BoothAdd, blade.Id));

            await buyer.Game.BuyFromBoothAsync(blade.Id, id);
            Assert.NotNull(await buyer.Game.WaitForTalkAsync(t => t.Text == "You don't have enough silver."));
            await EventAsync("booth.rejected", buyer.User.Name, "not_enough_money");

            await seller.DisposeAsync();
            Assert.True(await buyer.Game.WaitForRemovalAsync(id));
            await EventAsync("booth.closed", seller.User.Name, "logout");
        }
    }
}

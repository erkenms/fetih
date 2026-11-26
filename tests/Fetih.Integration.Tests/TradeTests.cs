namespace Fetih.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class TradeTests : IntegrationTest
    {
        public TradeTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task ATradeSwapsAnItemForSilver()
        {
            await using var seller = (await this.PlayingAtAsync(1002, 200, 200, GameMaster)).Player;
            await using var buyer = (await this.PlayingAtAsync(1002, 201, 200)).Player;
            var blade = await GiveAsync(seller, ServerFixture.Blade.Id);
            await OpenTradeAsync(seller, buyer);
            // The request also showed the buyer who asked
            var relation = await buyer.Game.WaitForRelationAsync(r => r.AskerId == seller.User.Id);
            Assert.Equal((buyer.User.Id, (uint)seller.User.Level), (relation?.TargetId, relation?.Level));

            await seller.Game.TradeAsync(TradeMessage.AddItem, blade.Id);
            var shown = await buyer.Game.WaitForItemAsync(i => i.Id == blade.Id);
            Assert.Equal((ushort)2, shown?.Action);
            await buyer.Game.TradeAsync(TradeMessage.SetSilver, 300);
            Assert.NotNull(await seller.Game.WaitForTradeAsync(t => t.Action == TradeMessage.ShowSilver && t.Data == 300));

            await seller.Game.TradeAsync(TradeMessage.Accept, 0);
            Assert.NotNull(await buyer.Game.WaitForTradeAsync(t => t.Action == TradeMessage.Accept && t.Data == seller.User.Id));
            await buyer.Game.TradeAsync(TradeMessage.Accept, 0);

            Assert.NotNull(await seller.Game.WaitForItemActionAsync(ItemMessage.Remove, blade.Id));
            var got = await buyer.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 1);
            Assert.NotNull(got);
            Assert.NotNull(await SilverAsync(seller, 1300));
            Assert.NotNull(await SilverAsync(buyer, 700));
            Assert.NotNull(await seller.Game.WaitForTradeAsync(t => t.Action == TradeMessage.HideTable));
            Assert.NotNull(await buyer.Game.WaitForTradeAsync(t => t.Action == TradeMessage.HideTable));
            Assert.Equal((int)buyer.User.Id, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT CharacterID FROM item WHERE ItemID = @id", ("@id", blade.Id))));

            var done = await this.Servers.WaitForEventAsync("game", "trade.completed", "name", seller.User.Name);
            Assert.Equal(buyer.User.Name, done.GetProperty("partner").GetString());
            Assert.Equal(new[] { blade.Id }, done.GetProperty("itemIds").EnumerateArray().Select(e => e.GetUInt32()));
            Assert.Equal(300u, done.GetProperty("partnerSilver").GetUInt32());
            Assert.Equal(0u, done.GetProperty("silver").GetUInt32());
        }

        [Fact]
        public async Task OffersAreCheckedAndClosingEndsTheTrade()
        {
            await using var first = (await this.PlayingAtAsync(1002, 210, 200)).Player;
            await using var second = (await this.PlayingAtAsync(1002, 211, 200)).Player;
            await OpenTradeAsync(first, second);

            await first.Game.TradeAsync(TradeMessage.AddItem, 12345);
            Assert.NotNull(await first.Game.WaitForTradeAsync(t => t.Action == TradeMessage.AddItemFailed && t.Data == 12345));
            await first.Game.TradeAsync(TradeMessage.SetSilver, 5000);
            Assert.NotNull(await first.Game.WaitForTalkAsync(t => t.Text == "You don't have that much silver."));

            await first.Game.TradeAsync(TradeMessage.Close, 0);
            Assert.NotNull(await first.Game.WaitForTradeAsync(t => t.Action == TradeMessage.HideTable && t.Data == second.User.Id));
            Assert.NotNull(await second.Game.WaitForTradeAsync(t => t.Action == TradeMessage.HideTable && t.Data == first.User.Id));
            var ended = await this.Servers.WaitForEventAsync("game", "trade.cancelled", "by", first.User.Name);
            Assert.Equal("closed", ended.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task OnlyNearbyPlayersTradeAndLeavingEndsTheTrade()
        {
            await using var first = (await this.PlayingAtAsync(1002, 220, 200)).Player;
            var second = (await this.PlayingAtAsync(1002, 221, 200)).Player;
            await using var far = (await this.PlayingAtAsync(1002, 260, 200)).Player;

            await first.Game.TradeAsync(TradeMessage.Request, far.User.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "trade.rejected", "name", first.User.Name);
            Assert.Equal("not_near", refused.GetProperty("reason").GetString());

            await OpenTradeAsync(first, second);
            await second.DisposeAsync();
            Assert.NotNull(await first.Game.WaitForTradeAsync(t => t.Action == TradeMessage.HideTable));
            var ended = await this.Servers.WaitForEventAsync("game", "trade.cancelled", "by", second.User.Name);
            Assert.Equal("logout", ended.GetProperty("reason").GetString());
        }
    }
}

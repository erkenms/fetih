namespace Fetih.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class TradePartnerTests : IntegrationTest
    {
        public TradePartnerTests(ServerFixture servers) : base(servers) { }

        private Task<object> RowsAsync(uint characterId, uint otherId) =>
            this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM trade_partner WHERE CharacterID = @a AND PartnerID = @b OR " +
                "CharacterID = @b AND PartnerID = @a", ("@a", characterId), ("@b", otherId));

        [Fact]
        public async Task PartnersAreKeptAcrossLoginsUntilOneBreaksUp()
        {
            await using var asker = (await this.PlayingAtAsync(1002, 240, 300)).Player;
            var (partner, email) = await this.PlayingAtAsync(1002, 241, 300);

            await asker.Game.TradeBuddyAsync(TradeBuddyMessage.Request, partner.User.Id);
            Assert.NotNull(await partner.Game.WaitForRelationAsync(r => r.AskerId == asker.User.Id));
            var asked = await partner.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Request);
            Assert.Equal((asker.User.Id, asker.User.Name), (asked?.Id, asked?.Name));
            await partner.Game.TradeBuddyAsync(TradeBuddyMessage.Request, asker.User.Id);
            var added = await asker.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Add);
            Assert.Equal((partner.User.Id, partner.User.Name, true), (added?.Id, added?.Name, added?.IsOnline));
            Assert.InRange(added.TrialMinutes, 3 * 24 * 60 - 1, 3 * 24 * 60);
            Assert.NotNull(await partner.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Add && b.Id == asker.User.Id));
            Assert.Equal(2, Convert.ToInt32(await this.RowsAsync(asker.User.Id, partner.User.Id)));
            await this.Servers.WaitForEventAsync("game", "trade_partner.added", "name", asker.User.Name);

            var info = await asker.Game.AskPartnerInfoAsync(asker.User.Id, partner.User.Id);
            Assert.Equal((partner.User.Id, (byte)partner.User.Level), (info?.Id, info?.Level));

            await partner.DisposeAsync();
            Assert.NotNull(await asker.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Offline && b.Id == partner.User.Id));
            await using (var back = await this.Servers.PlayAsync(email, Password))
            {
                var listed = back.Game.TradePartners.SingleOrDefault(b => b.Id == asker.User.Id);
                Assert.Equal((asker.User.Name, true), (listed?.Name, listed?.IsOnline));
                Assert.NotNull(await asker.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Online && b.Id == partner.User.Id));

                await asker.Game.TradeBuddyAsync(TradeBuddyMessage.Break, partner.User.Id);
                Assert.NotNull(await asker.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Break && b.Id == partner.User.Id));
                Assert.NotNull(await back.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Break && b.Id == asker.User.Id));
            }
            Assert.Equal(0, Convert.ToInt32(await this.RowsAsync(asker.User.Id, partner.User.Id)));
        }

        [Fact]
        public async Task ARefusalReachesTheAsker()
        {
            await using var asker = (await this.PlayingAtAsync(1002, 242, 300)).Player;
            await using var other = (await this.PlayingAtAsync(1002, 243, 300)).Player;
            await asker.Game.TradeBuddyAsync(TradeBuddyMessage.Request, other.User.Id);
            Assert.NotNull(await other.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Request));
            await other.Game.TradeBuddyAsync(TradeBuddyMessage.Reject, asker.User.Id);
            var refused = await asker.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Reject);
            Assert.Equal((other.User.Id, other.User.Name), (refused?.Id, refused?.Name));

            // The request is spent: asking back no longer partners them
            await other.Game.TradeBuddyAsync(TradeBuddyMessage.Request, asker.User.Id);
            Assert.NotNull(await asker.Game.WaitForTradeBuddyAsync(b => b.Action == TradeBuddyMessage.Request));
            Assert.Equal(0, Convert.ToInt32(await this.RowsAsync(asker.User.Id, other.User.Id)));
        }
    }
}

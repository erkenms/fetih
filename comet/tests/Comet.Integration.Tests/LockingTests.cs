namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class LockingTests : IntegrationTest
    {
        private const ushort GameMaster = 3;

        public LockingTests(ServerFixture servers) : base(servers) { }

        private static async Task<ItemInfo> LockedBladeAsync(Player player)
        {
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + ServerFixture.Blade.Id);
            var blade = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            Assert.NotNull(blade);
            await player.Game.LockAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForEquipLockAsync(l => l.ItemId == blade.Id && l.Mode == EquipLockMessage.Lock));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 3 && i.Locked));
            return blade;
        }

        [Fact]
        public async Task ALockStaysAcrossLogins()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 230, 300, GameMaster);
            var blade = await LockedBladeAsync(player);
            await this.Servers.WaitForEventAsync("game", "item.locked", "name", player.User.Name);
            await player.DisposeAsync();

            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.True(again.Inventory.Single(i => i.Id == blade.Id).Locked);
            Assert.Equal(1, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Locked FROM item WHERE ItemID = @id", ("@id", blade.Id))));
        }

        [Fact]
        public async Task OnlyEquipmentCanBeLocked()
        {
            await using var player = (await this.PlayingAtAsync(1002, 231, 300, GameMaster)).Player;
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + ServerFixture.Potion.Id);
            var potion = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Potion.Id);
            await player.Game.LockAsync(potion.Id);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "Only equipment can be locked!"));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Locked FROM item WHERE ItemID = @id", ("@id", potion.Id))));
        }

        [Fact]
        public async Task ALockedItemCantBeSoldOrDropped()
        {
            await using var player = (await this.PlayingAtAsync(1002, 452, 342, GameMaster)).Player;
            var blade = await LockedBladeAsync(player);

            await player.Game.DropAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "This equipment is locked. It can't be discarded."));
            await player.Game.SellAsync(ServerFixture.Blacksmith, blade.Id);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "This equipment is locked. It can't be sold."));
            Assert.Equal((int)player.User.Id, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT CharacterID FROM item WHERE ItemID = @id AND Position = 0", ("@id", blade.Id))));
        }

        [Fact]
        public async Task ALockedItemIsTradedOnlyToAFormalPartner()
        {
            var (seller, email) = await this.PlayingAtAsync(1002, 232, 300, GameMaster);
            await using var buyer = (await this.PlayingAtAsync(1002, 233, 300)).Player;
            var blade = await LockedBladeAsync(seller);
            await OpenTradeAsync(seller, buyer);
            await seller.Game.TradeAsync(TradeMessage.AddItem, blade.Id);
            Assert.NotNull(await seller.Game.WaitForTradeAsync(t => t.Action == TradeMessage.AddItemFailed && t.Data == blade.Id));
            Assert.NotNull(await seller.Game.WaitForTalkAsync(t =>
                t.Text == "This equipment is locked. You can only trade it to your trade partners."));
            await seller.DisposeAsync();

            // Partners for four days, past the trial
            await this.Servers.WaitForLogoutSaveAsync(seller.User.Name);
            await this.Servers.ExecuteGameSqlAsync(
                "INSERT INTO trade_partner (CharacterID, PartnerID, Since) VALUES " +
                "(@a, @b, UTC_TIMESTAMP() - INTERVAL 4 DAY), (@b, @a, UTC_TIMESTAMP() - INTERVAL 4 DAY)",
                ("@a", seller.User.Id), ("@b", buyer.User.Id));
            await using var partner = await this.Servers.PlayAsync(email, Password);
            Assert.Contains(partner.Game.TradePartners, p => p.Id == buyer.User.Id && p.TrialMinutes == 0);
            await OpenTradeAsync(partner, buyer);
            await partner.Game.TradeAsync(TradeMessage.AddItem, blade.Id);
            Assert.NotNull(await buyer.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 2));
            await partner.Game.TradeAsync(TradeMessage.Accept, 0);
            Assert.NotNull(await buyer.Game.WaitForTradeAsync(t => t.Action == TradeMessage.Accept));
            await buyer.Game.TradeAsync(TradeMessage.Accept, 0);
            var got = await buyer.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 1);
            Assert.True(got?.Locked);
        }

        [Fact]
        public async Task UnlockingTakesFiveDays()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 234, 300, GameMaster);
            var blade = await LockedBladeAsync(player);
            await player.Game.UnlockAsync(blade.Id);
            var date = await player.Game.WaitForEquipLockAsync(l => l.ItemId == blade.Id && l.Mode == EquipLockMessage.Unlock);
            var due = DateTime.UtcNow.AddDays(5);
            Assert.Equal((byte)3, date?.Flag);
            Assert.InRange(date.Value, (uint)(due.AddDays(-1).Year * 10000 + due.AddDays(-1).Month * 100 + due.AddDays(-1).Day),
                (uint)(due.Year * 10000 + due.Month * 100 + due.Day));
            await this.Servers.WaitForEventAsync("game", "item.unlock_started", "name", player.User.Name);

            // Still locked until then
            await player.Game.DropAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "This equipment is locked. It can't be discarded."));
            await player.DisposeAsync();

            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE item SET UnlocksAt = UTC_TIMESTAMP() - INTERVAL 1 MINUTE WHERE ItemID = @id", ("@id", blade.Id));
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.False(again.Inventory.Single(i => i.Id == blade.Id).Locked);
            Assert.NotNull(await again.Game.WaitForEquipLockAsync(l => l.ItemId == blade.Id && l.Mode == EquipLockMessage.Unlocked));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Locked FROM item WHERE ItemID = @id", ("@id", blade.Id))));
        }

        private static async Task OpenTradeAsync(Player asker, Player other)
        {
            Assert.NotNull(await asker.Game.WaitForSpawnAsync(other.User.Id));
            await asker.Game.TradeAsync(TradeMessage.Request, other.User.Id);
            Assert.NotNull(await other.Game.WaitForTradeAsync(t => t.Action == TradeMessage.Request && t.Data == asker.User.Id));
            await other.Game.TradeAsync(TradeMessage.Request, asker.User.Id);
            Assert.NotNull(await asker.Game.WaitForTradeAsync(t => t.Action == TradeMessage.ShowTable && t.Data == other.User.Id));
        }
    }
}

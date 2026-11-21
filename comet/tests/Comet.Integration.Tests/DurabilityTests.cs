namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class DurabilityTests : IntegrationTest
    {
        public DurabilityTests(ServerFixture servers) : base(servers) { }

        // A game master with a blade whose durability and silver are set while logged out
        private async Task<(Player Player, ItemInfo Blade)> BladeAtAsync(int x, int y, ushort amount, uint silver = 0)
        {
            var (first, email) = await this.PlayingAtAsync(1002, x, y, GameMaster, level: 100);
            var blade = await GiveAsync(first, ServerFixture.Blade.Id);
            var name = first.User.Name;
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync("UPDATE item SET Amount = @amount WHERE ItemID = @id",
                ("@amount", amount), ("@id", blade.Id));
            await this.Servers.ExecuteGameSqlAsync("UPDATE `character` SET Silver = @silver WHERE Name = @name",
                ("@silver", silver), ("@name", name));
            return (await this.Servers.PlayAsync(email, Password), blade);
        }

        [Fact]
        public async Task SwingsWearTheWeaponAndTheClientHearsOfEachHundred()
        {
            var at = ServerFixture.PheasantForWear;
            var (player, blade) = await BladeAtAsync(at.X + 1, at.Y, 100);
            await using var _ = player;
            await player.Game.EquipAsync(blade.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id));
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Amount == 99));
        }

        [Fact]
        public async Task WearWithinAHundredIsSavedAtLogout()
        {
            var at = ServerFixture.PheasantForSavedWear;
            var (player, blade) = await BladeAtAsync(at.X + 1, at.Y, 150);
            await player.Game.EquipAsync(blade.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id));
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Sender == player.User.Id));
            await this.LogOutAsync(player);
            var amount = await this.Servers.QueryGameScalarAsync(
                "SELECT Amount FROM item WHERE ItemID = @id", ("@id", blade.Id));
            Assert.True(System.Convert.ToInt32(amount) < 150);
        }

        [Fact]
        public async Task RepairsCostSilverForTheShareWorn()
        {
            var (player, blade) = await BladeAtAsync(600, 960, 500, silver: 1000);
            await using var _ = player;

            await player.Game.RepairAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Amount == ServerFixture.Blade.Amount));
            // 30 silver x 599/1099 worn x 75% for its quality, rounded up
            Assert.NotNull(await SilverAsync(player, 1000 - 13));
            var repaired = await this.Servers.WaitForEventAsync("game", "item.repaired", "name", player.User.Name);
            Assert.Equal("13 silver", repaired.GetProperty("paid").GetString());
        }

        [Fact]
        public async Task BrokenGearTakesFiveMeteors()
        {
            var (player, blade) = await BladeAtAsync(640, 960, 0);
            await using var _ = player;

            await player.Game.RepairAsync(blade.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.repair_rejected", "name", player.User.Name);
            Assert.Equal("no_meteors", refused.GetProperty("reason").GetString());

            for (int i = 0; i < 5; i++)
                await GiveAsync(player, ServerFixture.Meteor.Id);
            await player.Game.RepairAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Amount == ServerFixture.Blade.Amount));
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(1L, (long)count);
        }
    }
}

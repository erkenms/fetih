namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class SecondPasswordTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint Warehouseman = 8;

        public SecondPasswordTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task ThePasswordGuardsTheWarehouseAndUnlockingUntilGiven()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 410, 358, GameMaster);
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + ServerFixture.Blade.Id);
            var blade = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            await player.Game.LockAsync(blade.Id);
            Assert.NotNull(await player.Game.WaitForEquipLockAsync(l => l.Mode == EquipLockMessage.Lock));
            await player.Game.PasswordAsync(PasswordMessage.Set, 0, 1234);
            var set = await player.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Correct);
            Assert.Equal(PasswordMessage.HasPassword | PasswordMessage.Verified, set?.Password);
            await this.Servers.WaitForEventAsync("game", "second_password.set", "name", player.User.Name);
            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);

            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.NotNull(await again.Game.WaitForPasswordAsync(p =>
                p.Request == PasswordMessage.Correct && p.Password == PasswordMessage.HasPassword));
            await again.Game.SendPackageAsync(Warehouseman, PackageMessage.Deposit, blade.Id);
            Assert.NotNull(await again.Game.WaitForTalkAsync(t => t.Text == "Please input the secondary password."));
            Assert.NotNull(await again.Game.WaitForPasswordAsync(p => p.Password == PasswordMessage.HasPassword));
            Assert.Null(await again.Game.WaitForItemActionAsync(ItemMessage.Remove, blade.Id, TimeSpan.FromMilliseconds(300)));
            await again.Game.UnlockAsync(blade.Id);
            Assert.NotNull(await again.Game.WaitForPasswordAsync(p => p.Password == PasswordMessage.HasPassword));
            Assert.Null(await again.Game.WaitForEquipLockAsync(_ => true, TimeSpan.FromMilliseconds(300)));

            await again.Game.PasswordAsync(PasswordMessage.Verify, 0, 9999);
            Assert.NotNull(await again.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Wrong));
            await again.Game.PasswordAsync(PasswordMessage.Verify, 0, 1234);
            Assert.NotNull(await again.Game.WaitForPasswordAsync(p =>
                p.Request == PasswordMessage.Correct && p.Password == (PasswordMessage.HasPassword | PasswordMessage.Verified)));

            await again.Game.UnlockAsync(blade.Id);
            Assert.NotNull(await again.Game.WaitForEquipLockAsync(l => l.ItemId == blade.Id && l.Mode == EquipLockMessage.Unlock));
            await again.Game.SendPackageAsync(Warehouseman, PackageMessage.Deposit, blade.Id);
            Assert.NotNull(await again.Game.WaitForPackageAsync(p => p.Items.Any(i => i.Id == blade.Id)));
        }

        [Fact]
        public async Task ChangingNeedsTheOldPasswordAndThreeWrongTriesEndTheSession()
        {
            await using var player = (await this.PlayingAtAsync(1002, 235, 300)).Player;
            await player.Game.PasswordAsync(PasswordMessage.Set, 0, 1234);
            Assert.NotNull(await player.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Correct));

            await player.Game.PasswordAsync(PasswordMessage.Set, 1111, 5555);
            Assert.NotNull(await player.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Wrong));
            await player.Game.PasswordAsync(PasswordMessage.Set, 1234, 5555);
            Assert.NotNull(await player.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Correct));
            await this.Servers.WaitForEventAsync("game", "second_password.set", "name", player.User.Name);

            // One wrong try was spent on the change
            await player.Game.PasswordAsync(PasswordMessage.Set, 1, 2);
            await player.Game.PasswordAsync(PasswordMessage.Set, 1, 2);
            for (int i = 0; i < 2; i++)
                Assert.NotNull(await player.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Wrong));
            await player.Game.PasswordAsync(PasswordMessage.Set, 5555, 7777);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t =>
                t.Text == "Too many wrong secondary passwords. Log in again to retry."));
            Assert.Equal(5555, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT SecondPassword FROM `character` WHERE CharacterID = @id", ("@id", player.User.Id))));
        }
    }
}

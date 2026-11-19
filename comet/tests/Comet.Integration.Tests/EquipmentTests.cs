namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class EquipmentTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint RightHand = 4, LeftHand = 5, Headwear = 1;

        public EquipmentTests(ServerFixture servers) : base(servers) { }

        private static async Task<ItemInfo> GiveAsync(Player player, uint type)
        {
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + type);
            var item = await player.Game.WaitForItemAsync(i => i.TypeId == type);
            Assert.NotNull(item);
            return item;
        }

        private Task<object> PositionAsync(uint itemId) =>
            this.Servers.QueryGameScalarAsync("SELECT Position FROM item WHERE ItemID = @id", ("@id", itemId));

        [Fact]
        public async Task EquippedItemsAreShownAndKeptAfterLogout()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 620, 560, GameMaster);
            await using var observer = (await this.PlayingAtAsync(1002, 622, 560)).Player;
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);

            player.Game.ForgetKept();   // the login's empty DisplayGears
            await player.Game.EquipAsync(blade.Id, RightHand);
            var reply = await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id);
            Assert.Equal(RightHand, reply?.Data);
            Assert.NotNull(await observer.Game.WaitForSpawnAsync(player.User.Id,
                s => s.RightHand == ServerFixture.Blade.Id));
            var gears = await player.Game.WaitForItemActionAsync(ItemMessage.DisplayGears, player.User.Id);
            Assert.Equal(blade.Id, gears?.Gears[RightHand - 1]);
            Assert.Equal(4, System.Convert.ToInt32(await this.PositionAsync(blade.Id)));

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            await using var again = await this.Servers.PlayAsync(email, Password);
            var kept = Assert.Single(again.Inventory);
            Assert.Equal((blade.Id, (ushort)RightHand), (kept.Id, kept.Position));
            gears = await again.Game.WaitForItemActionAsync(ItemMessage.DisplayGears, player.User.Id);
            Assert.Equal(blade.Id, gears?.Gears[RightHand - 1]);

            // The observer sees the blade on the returning player; older spawns are dropped
            Assert.NotNull(await observer.Game.WaitForSpawnAsync(player.User.Id,
                s => s.RightHand == ServerFixture.Blade.Id));
            observer.Game.ForgetKept();

            await again.Game.UnequipAsync(blade.Id, RightHand);
            Assert.NotNull(await again.Game.WaitForItemActionAsync(ItemMessage.Unequip, blade.Id));
            Assert.NotNull(await observer.Game.WaitForSpawnAsync(player.User.Id,
                s => s.RightHand == 0));
            gears = await again.Game.WaitForItemActionAsync(ItemMessage.DisplayGears, player.User.Id);
            Assert.Equal(new uint[9], gears?.Gears);
            Assert.Equal(0, System.Convert.ToInt32(await this.PositionAsync(blade.Id)));
        }

        [Fact]
        public async Task ATwoHandedWeaponPutsBothHandsBackInTheInventory()
        {
            await using var player = (await this.PlayingAtAsync(1002, 640, 560, GameMaster)).Player;
            var right = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(right.Id, RightHand);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, right.Id));
            var left = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(left.Id, LeftHand);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, left.Id));

            var glaive = await GiveAsync(player, ServerFixture.Glaive.Id);
            await player.Game.EquipAsync(glaive.Id, RightHand);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Unequip, right.Id));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Unequip, left.Id));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, glaive.Id));
            Assert.Equal((0, 0, 4), (System.Convert.ToInt32(await this.PositionAsync(right.Id)),
                System.Convert.ToInt32(await this.PositionAsync(left.Id)),
                System.Convert.ToInt32(await this.PositionAsync(glaive.Id))));
        }

        [Fact]
        public async Task ItemsAboveTheCharactersLevelAreRefused()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 560, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            await player.Game.EquipAsync(helmet.Id, Headwear);
            var rejected = await this.Servers.WaitForEventAsync("game", "item.equip_rejected", "itemId", helmet.Id.ToString());
            Assert.Equal("level", rejected.GetProperty("reason").GetString());
            Assert.Null(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id, System.TimeSpan.FromMilliseconds(300)));
            Assert.Equal(0, System.Convert.ToInt32(await this.PositionAsync(helmet.Id)));
        }

        [Fact]
        public async Task TheLeftHandNeedsAOneHandedWeaponInTheRight()
        {
            await using var player = (await this.PlayingAtAsync(1002, 680, 560, GameMaster)).Player;
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(blade.Id, LeftHand);
            var rejected = await this.Servers.WaitForEventAsync("game", "item.equip_rejected", "itemId", blade.Id.ToString());
            Assert.Equal("needs_right_hand", rejected.GetProperty("reason").GetString());
        }
    }
}

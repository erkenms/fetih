namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class EquipmentTests : IntegrationTest
    {
        private const uint RightHand = 4, LeftHand = 5, Headwear = 1;

        public EquipmentTests(ServerFixture servers) : base(servers) { }

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

            await this.LogOutAsync(player);
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
            Assert.Equal(new uint[13], gears?.Gears);
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
        public async Task ObservingAPlayerShowsWhatTheyWear()
        {
            await using var player = (await this.PlayingAtAsync(1002, 720, 560, GameMaster)).Player;
            await using var observer = (await this.PlayingAtAsync(1002, 722, 560)).Player;
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(blade.Id, RightHand);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id));
            Assert.NotNull(await observer.Game.WaitForSpawnAsync(player.User.Id,
                s => s.RightHand == ServerFixture.Blade.Id));

            await observer.Game.ObserveAsync(observer.User.Id, player.User.Id);
            var worn = await observer.Game.WaitForBoothItemAsync(i => i.Id == blade.Id);
            Assert.Equal((player.User.Id, ServerFixture.Blade.Id, BoothItem.Worn, (ushort)RightHand),
                (worn?.BoothId, worn?.TypeId, worn?.View, worn?.Position));
            var spouse = await observer.Game.WaitForNameAsync(n => n.Action == NameMessage.Spouse);
            Assert.Equal((player.User.Id, "None"), (spouse?.Data, spouse?.Strings.FirstOrDefault()));
            await SyncAsync(observer);
            Assert.Null(await observer.Game.WaitForActionAsync(ActionMessage.Observe, observer.User.Id, Already));
        }

        [Fact]
        public async Task TheStatPaneAsksForThePlayersOwnStats()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 560, strength: 40)).Player;
            await player.Game.QueryStatsAsync(player.User.Id);
            var stats = await player.Game.WaitForStatsAsync(s => s.Id == player.User.Id);
            var user = player.User;
            uint maxHealth = (uint)(user.Vitality * 24 + (user.Strength + user.Agility + user.Spirit) * 3);
            Assert.Equal((maxHealth, user.Spirit * 5u, 40u, 40u, 0u, 0u, 0u, 0u, (uint)user.Agility),
                (stats?.MaxHealth, stats?.MaxMana, stats?.MaxAttack, stats?.MinAttack, stats?.Defense,
                    stats?.MagicAttack, stats?.MagicDefense, stats?.Dodge, stats?.Agility));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForTalkAsync(t => t.Text.StartsWith("Missing packet"), Already));
        }

        [Fact]
        public async Task WornGemsAndBlessingsShowInTheStatWindow()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 680, 940, GameMaster, level: 15);
            var helmet = await GiveAsync(first, ServerFixture.Helmet.Id);
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE item SET Gem1 = 13, Gem2 = 72, Bless = 3 WHERE ItemID = @id", ("@id", helmet.Id));
            await using var player = await this.Servers.PlayAsync(email, Password);

            await player.Game.EquipAsync(helmet.Id, 1);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id));
            await player.Game.QueryStatsAsync(player.User.Id);
            var stats = await player.Game.WaitForStatsAsync(s => s.Id == player.User.Id);
            Assert.Equal((15u, 0u, 4u, 3u), (stats?.AttackPercent, stats?.MagicPercent, stats?.ReducePercent, stats?.BlessPercent));
        }

        [Fact]
        public async Task TheStatQueryActionIsAnsweredWithTheStatWindowNotEchoed()
        {
            await using var player = (await this.PlayingAtAsync(1002, 760, 600)).Player;
            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.QueryStatInfo));
            Assert.NotNull(await player.Game.WaitForStatsAsync(s => s.Id == player.User.Id));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.QueryStatInfo, player.User.Id, Already));
        }

        [Fact]
        public async Task AnotherPlayersStatsCanBeAskedFor()
        {
            await using var player = (await this.PlayingAtAsync(1002, 780, 600, strength: 25)).Player;
            await using var observer = (await this.PlayingAtAsync(1002, 782, 600)).Player;
            await observer.Game.QueryStatsAsync(player.User.Id);
            var stats = await observer.Game.WaitForStatsAsync(s => s.Id == player.User.Id);
            Assert.Equal((25u, 25u), (stats?.MinAttack, stats?.MaxAttack));

            // Nobody online by that ID: nothing
            await observer.Game.QueryStatsAsync(999);
            await SyncAsync(observer);
            Assert.Null(await observer.Game.WaitForStatsAsync(s => s.Id == 999, Already));
        }

        [Fact]
        public async Task TheStatWindowCountsWornItems()
        {
            await using var player = (await this.PlayingAtAsync(1002, 740, 620, GameMaster)).Player;
            var bottle = await GiveAsync(player, ServerFixture.Bottle.Id);
            await player.Game.EquipAsync(bottle.Id, 7);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bottle.Id));

            await player.Game.QueryStatsAsync(player.User.Id);
            uint mana = player.User.Spirit * 5u + ServerFixture.Bottle.Mana;
            Assert.NotNull(await player.Game.WaitForStatsAsync(s => s.Id == player.User.Id && s.MaxMana == mana));
        }

        [Fact]
        public async Task ItemsAboveTheCharactersLevelAreRefused()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 560, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            await player.Game.EquipAsync(helmet.Id, Headwear);
            var rejected = await this.Servers.WaitForEventAsync("game", "item.equip_rejected", "itemId", helmet.Id.ToString());
            Assert.Equal("level", rejected.GetProperty("reason").GetString());
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id, Already));
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

        [Fact]
        public async Task AQuiverIsWornWhenUsedAndShownAgainWhenTakenOff()
        {
            await using var player = (await this.PlayingAtAsync(1002, 740, 960, GameMaster)).Player;
            var bow = await GiveAsync(player, ServerFixture.Bow.Id);
            await player.Game.EquipAsync(bow.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bow.Id));
            var arrows = await GiveAsync(player, ServerFixture.Arrows.Id);

            // The client sends a quiver's right click as a use, slot 0
            await player.Game.UseAsync(arrows.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, arrows.Id));

            await player.Game.UnequipAsync(arrows.Id, 5);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Unequip, arrows.Id));
            var shown = await player.Game.WaitForItemAsync(i => i.Id == arrows.Id);
            Assert.Equal(ServerFixture.Arrows.Amount, shown?.Amount);
        }
    }
}

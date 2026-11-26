namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    // The skills a second rebirth leaves, with the test magictype.dat of ServerFixture
    public class PureSkillTests : IntegrationTest
    {
        public PureSkillTests(ServerFixture servers) : base(servers) { }

        private static async Task LearnAsync(Player player, ushort magicType)
        {
            await CommandAsync(player, $"/learn {magicType}");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == magicType));
        }

        private static async Task<ItemInfo> HoldAsync(Player player, uint itemType, byte slot)
        {
            var item = await GiveAsync(player, itemType);
            await player.Game.EquipAsync(item.Id, slot);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, item.Id));
            return item;
        }

        [Fact]
        public async Task DragonWhirlLeapsToTheCellAimedAtAndHitsAllAroundIt()
        {
            await using var trojan = (await this.PlayingAtAsync(ServerFixture.PkField, 60, 90, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 67, 90)).Player;
            await FreePkAsync(trojan);
            await LearnAsync(trojan, 10315);

            await trojan.Game.CastAsync(trojan.User.Id, 10315, 0, 65, 90);
            var effect = await victim.Game.WaitForEffectAsync(e => e.Caster == trojan.User.Id && e.Type == 10315);
            Assert.Equal((65, 90), (effect.X, effect.Y));
            Assert.Equal(victim.User.Id, Assert.Single(effect.Targets).Id);
            Assert.Equal((ServerFixture.PkField, 65, 90), await this.SavedPositionAsync(trojan));
        }

        [Fact]
        public async Task StarArrowThrowsTheTargetBackToItsRangeFromTheArcher()
        {
            await using var archer = (await this.PlayingAtAsync(ServerFixture.PkField, 60, 80, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 63, 80)).Player;
            await FreePkAsync(archer);
            await LearnAsync(archer, 10313);
            await HoldAsync(archer, ServerFixture.Bow.Id, 4);
            var arrows = await HoldAsync(archer, ServerFixture.Arrows.Id, 5);

            await archer.Game.CastAsync(archer.User.Id, 10313, victim.User.Id, 63, 80);
            var effect = await victim.Game.WaitForEffectAsync(e => e.Caster == archer.User.Id && e.Type == 10313);
            Assert.Equal((66, 80), (effect.X, effect.Y));
            Assert.Equal(victim.User.Id, Assert.Single(effect.Targets).Id);
            Assert.NotNull(await archer.Game.WaitForItemAsync(i => i.Id == arrows.Id && i.Amount == ServerFixture.Arrows.Amount - 1));
            await SyncAsync(victim);
            Assert.Equal((ServerFixture.PkField, 66, 80), await this.SavedPositionAsync(victim));
        }

        [Fact]
        public async Task PerseveranceLetsAWarriorHoldAShieldWithATwoHandedWeapon()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 760, 740, GameMaster);
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync("UPDATE `character` SET CurrentClass = 21 WHERE Name = @name",
                ("@name", first.User.Name));
            await using var warrior = await this.Servers.PlayAsync(email, Password);
            await HoldAsync(warrior, ServerFixture.Glaive.Id, 4);

            var shield = await GiveAsync(warrior, ServerFixture.Shield.Id);
            await warrior.Game.EquipAsync(shield.Id, 5);
            var refused = await this.Servers.WaitForEventAsync("game", "item.equip_rejected", "name", warrior.User.Name);
            Assert.Equal("needs_right_hand", refused.GetProperty("reason").GetString());

            await LearnAsync(warrior, 10311);
            await warrior.Game.EquipAsync(shield.Id, 5);
            Assert.NotNull(await warrior.Game.WaitForItemActionAsync(ItemMessage.Equipped, shield.Id));
        }
    }
}

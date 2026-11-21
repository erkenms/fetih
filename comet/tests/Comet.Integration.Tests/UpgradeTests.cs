namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class UpgradeTests : IntegrationTest
    {
        private const uint Artisan = 10062, DragonBallUpgrader = 7050;
        private const byte HeadgearSlot = 1;

        public UpgradeTests(ServerFixture servers) : base(servers) { }

        // A game master in the market wearing the level-15 helmet, with gems
        private async Task<(Player Player, ItemInfo Helmet)> WearingHelmetAsync(int x, byte level, uint gemType, int gems)
        {
            var player = (await this.PlayingAtAsync(ServerFixture.Market, x, 205, GameMaster, level)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            await player.Game.EquipAsync(helmet.Id, HeadgearSlot);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id));
            for (int i = 0; i < gems; i++)
                await GiveAsync(player, gemType);
            return (player, helmet);
        }

        private static async Task<string> AnswerAsync(Player player, byte option)
        {
            await player.Game.AnswerNpcAsync(option);
            return (await player.Game.ReadDialogAsync())?[0].Text;
        }

        [Fact]
        public async Task TheArtisanRaisesWornGearsLevelForMeteorsAndQualityForDragonballs()
        {
            var (player, helmet) = await WearingHelmetAsync(178, 20, ServerFixture.Meteor.Id, 2);
            await using var _ = player;

            await player.Game.TalkToNpcAsync(Artisan);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            Assert.NotNull(await AnswerAsync(player, 1));                        // upgrade level
            Assert.Contains("2 meteors", await AnswerAsync(player, 20 + HeadgearSlot));
            Assert.Contains("done", await AnswerAsync(player, 10 + HeadgearSlot));
            Assert.NotNull(await player.Game.WaitForItemAsync(i =>
                i.Id == helmet.Id && i.TypeId == ServerFixture.HelmetLevelUp.Id));

            for (int i = 0; i < 2; i++)
                await GiveAsync(player, ServerFixture.DragonBall.Id);
            await player.Game.TalkToNpcAsync(Artisan);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            Assert.NotNull(await AnswerAsync(player, 3));                        // upgrade quality
            Assert.Contains("2 dragonballs", await AnswerAsync(player, 30 + HeadgearSlot));
            Assert.Contains("done", await AnswerAsync(player, 40 + HeadgearSlot));
            Assert.NotNull(await player.Game.WaitForItemAsync(i =>
                i.Id == helmet.Id && i.TypeId == ServerFixture.HelmetRefined.Id));

            var upgraded = await this.Servers.WaitForEventAsync("game", "item.upgraded", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("upgrade").GetString() == "quality");
            Assert.Equal(ServerFixture.HelmetRefined.Id, upgraded.GetProperty("typeId").GetUInt32());
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(1L, (long)count);
        }

        [Fact]
        public async Task GearIsntUpgradedPastThePlayersLevel()
        {
            var (player, helmet) = await WearingHelmetAsync(182, 15, ServerFixture.DragonBall.Id, 1);
            await using var _ = player;

            await player.Game.TalkToNpcAsync(DragonBallUpgrader);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            Assert.Contains("past your own level", await AnswerAsync(player, 20 + HeadgearSlot));
            var refused = await this.Servers.WaitForEventAsync("game", "item.upgrade_rejected", "name", player.User.Name);
            Assert.Equal("above_level", refused.GetProperty("reason").GetString());
            Assert.NotNull(await this.Servers.QueryGameScalarAsync(
                "SELECT ItemID FROM item WHERE CharacterID = @id AND TypeID = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.DragonBall.Id)));
        }
    }
}

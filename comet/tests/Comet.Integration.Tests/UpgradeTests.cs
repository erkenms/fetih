namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class UpgradeTests : IntegrationTest
    {
        public UpgradeTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task MeteorsRaiseLevelsAndDragonballsQualityAndAreUsedUp()
        {
            await using var player = (await this.PlayingAtAsync(1002, 600, 940, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var meteor = await GiveAsync(player, ServerFixture.Meteor.Id);

            await player.Game.LevelUpAsync(helmet.Id, meteor.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, meteor.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i =>
                i.Id == helmet.Id && i.TypeId == ServerFixture.HelmetLevelUp.Id));
            var upgraded = await this.Servers.WaitForEventAsync("game", "item.upgraded", "name", player.User.Name);
            Assert.Equal(("level", "success", ServerFixture.HelmetLevelUp.Id), (upgraded.GetProperty("upgrade").GetString(),
                upgraded.GetProperty("result").GetString(), upgraded.GetProperty("typeId").GetUInt32()));

            var dragonBall = await GiveAsync(player, ServerFixture.DragonBall.Id);
            await player.Game.ImproveAsync(helmet.Id, dragonBall.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, dragonBall.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i =>
                i.Id == helmet.Id && i.TypeId == ServerFixture.HelmetRefined.Id));

            var kept = await this.Servers.QueryGameScalarAsync(
                "SELECT TypeID FROM item WHERE ItemID = @id", ("@id", helmet.Id));
            Assert.Equal(ServerFixture.HelmetRefined.Id, (uint)kept);
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(1L, (long)count);
        }

        [Fact]
        public async Task OnlyDragonballsRaiseQuality()
        {
            await using var player = (await this.PlayingAtAsync(1002, 640, 940, GameMaster)).Player;
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            var meteor = await GiveAsync(player, ServerFixture.Meteor.Id);

            await player.Game.ImproveAsync(helmet.Id, meteor.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.upgrade_rejected", "name", player.User.Name);
            Assert.Equal("wrong_gem", refused.GetProperty("reason").GetString());
            Assert.NotNull(await this.Servers.QueryGameScalarAsync(
                "SELECT ItemID FROM item WHERE ItemID = @id", ("@id", meteor.Id)));
        }
    }
}

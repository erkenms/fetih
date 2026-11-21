namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class BlessingTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const double Day = 24 * 60 * 60;

        public BlessingTests(ServerFixture servers) : base(servers) { }

        private static async Task PrayAsync(Player player, uint stoneType)
        {
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + stoneType);
            var stone = await player.Game.WaitForItemAsync(i => i.TypeId == stoneType);
            Assert.NotNull(stone);
            // The 5619 client sends slot 17 for a praying stone
            await player.Game.EquipAsync(stone.Id, 17);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, stone.Id));
        }

        private static Task<UserAttribMessage> SecondsLeftAsync(Player player, double moreThanDays) =>
            player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(HeavenBlessing, out var s) && s > moreThanDays * Day);

        [Fact]
        public async Task PrayingStonesAddBlessingThatOutlastsTheSession()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 620, 580, GameMaster);
            await PrayAsync(player, ServerFixture.SmallPrayingStone.Id);
            var left = await SecondsLeftAsync(player, 2.99);
            Assert.True(left.Values[HeavenBlessing] <= 3 * Day);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & Blessed) != 0));
            var blessed = await this.Servers.WaitForEventAsync("game", "character.blessed", "name", player.User.Name);
            Assert.Equal("praying_stone", blessed.GetProperty("reason").GetString());

            // A second stone adds to what's left
            await PrayAsync(player, ServerFixture.PrayingStone.Id);
            Assert.NotNull(await SecondsLeftAsync(player, 9.99));

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            Assert.IsType<DateTime>(await this.Servers.QueryGameScalarAsync(
                "SELECT HeavenBlessing FROM `character` WHERE CharacterID = @id", ("@id", player.User.Id)));
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.NotNull(await SecondsLeftAsync(again, 9.99));
            Assert.NotNull(await again.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & Blessed) != 0));
        }

        [Fact]
        public async Task TheBlessedReviveWhereTheyDied()
        {
            var at = ServerFixture.GuardToProvoke;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster)).Player;
            var guard = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(guard);
            await PrayAsync(player, ServerFixture.SmallPrayingStone.Id);
            Assert.NotNull(await SecondsLeftAsync(player, 2.99));

            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.PkMode, command: 0));
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.PkMode, player.User.Id));
            await player.Game.AttackAsync(player.User.Id, guard.Id, at.X, at.Y);
            await this.Servers.WaitForEventAsync("game", "character.died", "name", player.User.Name);

            await Task.Delay(TimeSpan.FromSeconds(ServerFixture.ReviveSeconds));
            await player.Game.ReviveHereAsync(player.User.Id);
            var revived = await this.Servers.WaitForEventAsync("game", "character.revived", "name", player.User.Name);
            Assert.Equal("button_here", revived.GetProperty("reason").GetString());
            Assert.Equal((1002u, at.X + 1, (int)at.Y), (revived.GetProperty("mapId").GetUInt32(),
                revived.GetProperty("x").GetInt32(), revived.GetProperty("y").GetInt32()));
        }
    }
}

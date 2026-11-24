namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class RefineryTests : IntegrationTest
    {
        private const uint Stabilizer = 7538;
        private const uint StabilizationWindow = 448;
        private const uint Intensification = 301;
        private const uint ModeRefine = 0, ModeExpire = 1, ModeStable = 3, ModeStabilized = 4;
        private const int Headwear = 1;

        public RefineryTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task TheStabilizerOpensTheStabilizationWindow()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.Market, 299, 215)).Player;
            await player.Game.TalkToNpcAsync(Stabilizer);
            Assert.NotNull(await player.Game.ReadDialogAsync());

            await player.Game.AnswerNpcAsync(1);
            var open = await player.Game.WaitForActionAsync(ActionMessage.OpenWindow, player.User.Id);
            Assert.NotNull(open);
            Assert.Equal(StabilizationWindow, open.Command);
        }

        [Fact]
        public async Task RefiningWornGearRaisesMaxHealthAndStabilizingMakesItPermanent()
        {
            var (player, email) = await this.PlayingAtAsync(ServerFixture.Market, 299, 216, GameMaster, 20);
            var helmet = await GiveAsync(player, ServerFixture.Helmet.Id);
            await player.Game.EquipAsync(helmet.Id, Headwear);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id));

            // The wrong gear: a blade's refinery on a helmet
            var blade = await GiveAsync(player, ServerFixture.BladeRefinery.Id);
            await player.Game.RefineAsync(helmet.Id, blade.Id);
            var wrong = await this.Servers.WaitForEventAsync("game", "item.refine_rejected",
                e => e.GetProperty("name").GetString() == player.User.Name);
            Assert.Equal("wrong_gear", wrong.GetProperty("reason").GetString());

            // Stabilizing without a temporary state
            var stone = await GiveAsync(player, ServerFixture.PermanentStone.Id);
            await player.Game.StabilizeAsync(helmet.Id, stone.Id);
            await this.Servers.WaitForEventAsync("game", "item.refine_rejected",
                e => e.GetProperty("name").GetString() == player.User.Name &&
                     e.GetProperty("reason").GetString() == "no_temporary");

            var refinery = await GiveAsync(player, ServerFixture.HeadgearRefinery.Id);
            await player.Game.RefineAsync(helmet.Id, refinery.Id);
            var status = await player.Game.WaitForItemStatusAsync(s => s.ItemId == helmet.Id && s.Mode == ModeRefine);
            Assert.NotNull(status);
            Assert.Equal(Intensification, status.Kind);
            Assert.Equal(1u, status.Level);
            Assert.Equal(ServerFixture.HeadgearRefineryPower, status.Power);
            Assert.InRange(status.Seconds, 7u * 86400 - 60, 7u * 86400);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, refinery.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                a.Values.TryGetValue(MaxHealth, out _)));
            Assert.Equal((long)Intensification, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT RefineryKind FROM item WHERE ItemID = @id", ("@id", helmet.Id))));

            await player.Game.StabilizeAsync(helmet.Id, stone.Id);
            var stable = await player.Game.WaitForItemStatusAsync(s => s.ItemId == helmet.Id && s.Mode == ModeStabilized);
            Assert.NotNull(stable);
            Assert.Equal(Intensification, stable.Kind);
            Assert.Equal(ServerFixture.HeadgearRefineryPower, stable.Power);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, stone.Id));
            Assert.Equal((long)Intensification, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT StableKind FROM item WHERE ItemID = @id AND RefineryKind = 0", ("@id", helmet.Id))));

            // A new temporary state that ran out while away ends after login; the permanent one
            // comes with the item
            var again = await GiveAsync(player, ServerFixture.HeadgearRefinery.Id);
            await player.Game.RefineAsync(helmet.Id, again.Id);
            Assert.NotNull(await player.Game.WaitForItemStatusAsync(s => s.ItemId == helmet.Id && s.Mode == ModeRefine));
            await this.LogOutAsync(player);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE item SET RefineryExpiresAt = '2020-01-01' WHERE ItemID = @id", ("@id", helmet.Id));

            await using var back = await this.Servers.PlayAsync(email, Password);
            Assert.NotNull(await back.Game.WaitForItemStatusAsync(s => s.ItemId == helmet.Id && s.Mode == ModeStable));
            Assert.NotNull(await back.Game.WaitForItemStatusAsync(s => s.ItemId == helmet.Id && s.Mode == ModeExpire,
                TimeSpan.FromSeconds(5)));
            await this.Servers.WaitForEventAsync("game", "item.refinery_expired",
                e => e.GetProperty("name").GetString() == back.User.Name);
            Assert.Equal(0L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT RefineryKind FROM item WHERE ItemID = @id", ("@id", helmet.Id))));
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class DegradeEquipmentTests : IntegrationTest
    {
        private const uint Artisan = 10062;
        private const uint DegradeWindow = 506;

        public DegradeEquipmentTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task MagicArtisanOpensTheDegradeEquipmentWindow()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.Market, 179, 206)).Player;
            await player.Game.TalkToNpcAsync(Artisan);
            Assert.NotNull(await player.Game.ReadDialogAsync());

            await player.Game.AnswerNpcAsync(5);
            var open = await player.Game.WaitForActionAsync(ActionMessage.OpenWindow, player.User.Id);
            Assert.NotNull(open);
            Assert.Equal(DegradeWindow, open.Command);
        }

        [Fact]
        public async Task DegradeLowersGearToLevel15For54CPsKeepingQualityPlusAndSockets()
        {
            var (first, email) = await this.PlayingAtAsync(ServerFixture.Market, 180, 206, GameMaster);
            var helmet = await GiveAsync(first, ServerFixture.HelmetRefined.Id);
            await this.LogOutAsync(first);

            // Give the helmet +3, two sockets, and damaged durability to test repair refusal first
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE item SET Plus = 3, Gem1 = 13, Gem2 = 255, Amount = 100 WHERE ItemID = @id",
                ("@id", helmet.Id));

            await using var player = await this.Servers.PlayAsync(email, Password);

            // Refusal: damaged equipment
            await player.Game.DegradeItemAsync(helmet.Id);
            var repairEvt = await this.Servers.WaitForEventAsync("game", "item.degrade_rejected",
                e => e.GetProperty("name").GetString() == player.User.Name &&
                     e.GetProperty("reason").GetString() == "needs_repair");
            Assert.Equal("needs_repair", repairEvt.GetProperty("reason").GetString());

            // Repair durability in DB and relog
            await this.LogOutAsync(player);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE item SET Amount = AmountLimit WHERE ItemID = @id", ("@id", helmet.Id));
            await using var repaired = await this.Servers.PlayAsync(email, Password);

            // Refusal: not enough CPs
            await repaired.Game.DegradeItemAsync(helmet.Id);
            var cpEvt = await this.Servers.WaitForEventAsync("game", "item.degrade_rejected",
                e => e.GetProperty("name").GetString() == repaired.User.Name &&
                     e.GetProperty("reason").GetString() == "no_cps");
            Assert.Equal("no_cps", cpEvt.GetProperty("reason").GetString());

            // Grant 100 CPs and degrade
            await CommandAsync(repaired, "/jewel 100");
            Assert.NotNull(await repaired.Game.WaitForAttribAsync(a =>
                a.Id == repaired.User.Id && a.Values.TryGetValue(Jewels, out var j) && j == 100));

            await repaired.Game.DegradeItemAsync(helmet.Id);
            var updated = await repaired.Game.WaitForItemAsync(i =>
                i.Id == helmet.Id && i.TypeId == ServerFixture.HelmetRefinedLowest.Id);
            Assert.NotNull(updated);
            Assert.Equal((byte)3, updated.Plus);
            Assert.Equal((byte)13, updated.Gem1);
            Assert.Equal((byte)255, updated.Gem2);

            var cpsAfter = await repaired.Game.WaitForAttribAsync(a =>
                a.Id == repaired.User.Id && a.Values.TryGetValue(Jewels, out var j) && j == 46);
            Assert.NotNull(cpsAfter);

            // Refusal: already at lowest level
            await repaired.Game.DegradeItemAsync(helmet.Id);
            var lowestEvt = await this.Servers.WaitForEventAsync("game", "item.degrade_rejected",
                e => e.GetProperty("name").GetString() == repaired.User.Name &&
                     e.GetProperty("reason").GetString() == "lowest");
            Assert.Equal("lowest", lowestEvt.GetProperty("reason").GetString());

            // Persisted in DB
            Assert.Equal((long)ServerFixture.HelmetRefinedLowest.Id, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT TypeID FROM item WHERE ItemID = @id", ("@id", helmet.Id))));
        }
    }
}

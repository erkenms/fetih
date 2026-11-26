namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    // Steeds and the Riding skill, with the test Steed and Riding of ServerFixture
    public class MountTests : IntegrationTest
    {
        private const uint SteedSlot = 12, CropSlot = 18;
        private const ushort RidingSkill = 7001;
        private const ulong RidingStatus = 1UL << 50;
        private const uint MountTrainer = 5600;

        public MountTests(ServerFixture servers) : base(servers) { }

        private static Task<UserAttribMessage> RidingAsync(Player observer, uint riderId, bool riding) =>
            observer.Game.WaitForAttribAsync(a => a.Id == riderId &&
                a.Values.TryGetValue(Status, out var s) && ((s & RidingStatus) != 0) == riding);

        private static async Task<ItemInfo> EquipSteedAsync(Player player, byte plus)
        {
            await CommandAsync(player, $"/steed {plus}");
            var steed = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Steed.Id);
            await player.Game.EquipAsync(steed.Id, SteedSlot);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, steed.Id));
            return steed;
        }

        private async Task<string> RejectedAsync(Player player) =>
            (await this.Servers.WaitForEventAsync("game", "magic.rejected", "name", player.User.Name))
                .GetProperty("reason").GetString();

        private Task<System.Text.Json.JsonElement> DismountedAsync(Player player, string reason) =>
            this.Servers.WaitForEventAsync("game", "mount.ended", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("reason").GetString() == reason);

        [Fact]
        public async Task RidingASteedSpendsVigorAndEndsWhenCastAgainOrTakenOff()
        {
            await using var watcher = (await this.PlayingAtAsync(ServerFixture.PkField, 20, 86)).Player;
            await using var rider = (await this.PlayingAtAsync(ServerFixture.PkField, 20, 80, GameMaster)).Player;
            await CommandAsync(rider, "/learn 7001");
            Assert.NotNull(await rider.Game.WaitForMagicInfoAsync(m => m.Type == RidingSkill));

            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 20, 80);
            Assert.Equal("no_steed", await this.RejectedAsync(rider));

            var steed = await EquipSteedAsync(rider, 1);
            var shown = await watcher.Game.WaitForSpawnAsync(rider.User.Id, s => s.Steed == ServerFixture.Steed.Id);
            Assert.Equal((byte)1, shown?.SteedPlus);

            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 20, 80);
            Assert.NotNull(await rider.Game.WaitForEffectAsync(e => e.Caster == rider.User.Id && e.Type == RidingSkill));
            Assert.NotNull(await rider.Game.WaitForVigorAsync(v => v == ServerFixture.SteedVigor));
            Assert.NotNull(await rider.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Stamina, out var s) && s == 80));
            Assert.NotNull(await RidingAsync(rider, rider.User.Id, true));
            Assert.NotNull(await RidingAsync(watcher, rider.User.Id, true));

            // Direction 0 of 24 is two cells south
            await rider.Game.RideAsync(rider.User.Id, 0);
            var step = await watcher.Game.WaitForWalkAsync(rider.User.Id);
            Assert.Equal(WalkMessage.Ride, step?.Mode);
            Assert.NotNull(await rider.Game.WaitForVigorAsync(v => v == ServerFixture.SteedVigor - 2));
            var walked = await this.Servers.WaitForEventAsync("game", "move.walk", "name", rider.User.Name);
            Assert.Equal((20, 82), (walked.GetProperty("x").GetInt32(), walked.GetProperty("y").GetInt32()));

            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 20, 82);
            Assert.NotNull(await RidingAsync(watcher, rider.User.Id, false));
            await this.DismountedAsync(rider, "magic");

            await rider.Game.RideAsync(rider.User.Id, 0);
            var refused = await this.Servers.WaitForEventAsync("game", "move.rejected", "name", rider.User.Name);
            Assert.Equal("not_riding", refused.GetProperty("reason").GetString());

            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 20, 82);
            Assert.NotNull(await RidingAsync(watcher, rider.User.Id, true));
            await rider.Game.UnequipAsync(steed.Id, SteedSlot);
            Assert.NotNull(await RidingAsync(watcher, rider.User.Id, false));
            await this.DismountedAsync(rider, "steed_changed");
        }

        [Fact]
        public async Task ARiderWithACropAttacksWithoutGettingOff()
        {
            await using var target = (await this.PlayingAtAsync(ServerFixture.PkField, 30, 81)).Player;
            await using var rider = (await this.PlayingAtAsync(ServerFixture.PkField, 30, 80, GameMaster)).Player;
            await CommandAsync(rider, "/learn 7001");
            Assert.NotNull(await rider.Game.WaitForMagicInfoAsync(m => m.Type == RidingSkill));
            await EquipSteedAsync(rider, 1);
            var crop = await GiveAsync(rider, ServerFixture.Crop.Id);
            rider.Game.ForgetKept();    // the steed's DisplayGears
            await rider.Game.EquipAsync(crop.Id, CropSlot);
            Assert.NotNull(await rider.Game.WaitForItemActionAsync(ItemMessage.Equipped, crop.Id));
            var gears = await rider.Game.WaitForItemActionAsync(ItemMessage.DisplayGears, rider.User.Id);
            Assert.Equal(crop.Id, gears?.Gears[12]);

            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 30, 80);
            Assert.NotNull(await rider.Game.WaitForVigorAsync(v => v == ServerFixture.SteedVigor + ServerFixture.CropVigor));
            await rider.Game.AttackAsync(rider.User.Id, target.User.Id, 30, 81);
            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 30, 80);
            var ended = await this.Servers.WaitForEventAsync("game", "mount.ended", "name", rider.User.Name);
            Assert.Equal("magic", ended.GetProperty("reason").GetString());

            // Taking the crop off brings the vigor down to the steed's
            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 30, 80);
            Assert.NotNull(await RidingAsync(rider, rider.User.Id, true));
            await rider.Game.UnequipAsync(crop.Id, CropSlot);
            Assert.NotNull(await rider.Game.WaitForVigorAsync(v => v == ServerFixture.SteedVigor));
            await rider.Game.AttackAsync(rider.User.Id, target.User.Id, 30, 81);
            await this.DismountedAsync(rider, "attack");
        }

        [Fact]
        public async Task TwinCityNeedsASteedOfLineageLevelFour()
        {
            await using var rider = (await this.PlayingAtAsync(1002, 700, 900, GameMaster)).Player;
            await CommandAsync(rider, "/learn 7001");
            Assert.NotNull(await rider.Game.WaitForMagicInfoAsync(m => m.Type == RidingSkill));

            await EquipSteedAsync(rider, 1);
            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 700, 900);
            Assert.Equal("lineage_level", await this.RejectedAsync(rider));

            await EquipSteedAsync(rider, 4);
            await rider.Game.CastAsync(rider.User.Id, RidingSkill, 0, 700, 900);
            Assert.NotNull(await RidingAsync(rider, rider.User.Id, true));
        }

        [Fact]
        public async Task TheMountTrainerTeachesRidingForSilver()
        {
            await using var player = (await this.PlayingAtAsync(1002, 390, 388, GameMaster)).Player;
            await CommandAsync(player, "/silver +49000");
            Assert.NotNull(await SilverAsync(player, 50000));

            await player.Game.TalkToNpcAsync(MountTrainer);
            Assert.Contains("50,000 silver", (await player.Game.ReadDialogAsync())?[0].Text);
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == RidingSkill));
            Assert.NotNull(await SilverAsync(player, 0));
            Assert.Contains("learned Riding", (await player.Game.ReadDialogAsync())?[0].Text);

            await player.Game.TalkToNpcAsync(MountTrainer);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.Contains("already know", (await player.Game.ReadDialogAsync())?[0].Text);
        }
    }
}

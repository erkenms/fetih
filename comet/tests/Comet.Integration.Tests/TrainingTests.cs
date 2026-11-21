namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class TrainingTests : IntegrationTest
    {
        private const ushort GameMaster = 3;

        public TrainingTests(ServerFixture servers) : base(servers) { }

        private static async Task PrayAsync(Player player)
        {
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/item " + ServerFixture.SmallPrayingStone.Id);
            var stone = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.SmallPrayingStone.Id);
            await player.Game.EquipAsync(stone.Id, 17);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.ContainsKey(HeavenBlessing)));
        }

        private Task<System.Text.Json.JsonElement> ClaimedAsync(Player player, string kind) =>
            this.Servers.WaitForEventAsync("game", "training.exp_claimed", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("kind").GetString() == kind);

        [Fact]
        public async Task TheBlessedFillHuntingAndOnlineTrainingPoolsToClaim()
        {
            var at = ServerFixture.PheasantToKill;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, strength: 5000)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await PrayAsync(player);

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id));
            Assert.NotNull(await player.Game.WaitForGodExpAsync(g => g.Hunting > 0));
            await player.Game.GodExpAsync(GodExpMessage.ClaimHunting);
            Assert.NotNull(await player.Game.WaitForGodExpAsync(g => g.Hunting == 0));
            Assert.True(await ClaimedAsync(player, "blessed_hunting") is { ValueKind: System.Text.Json.JsonValueKind.Object });

            var online = await player.Game.WaitForGodExpAsync(g => g.Online > 0,
                TimeSpan.FromSeconds(ServerFixture.TrainingOnlineSeconds * 3 + 2));
            Assert.NotNull(online);
            await player.Game.GodExpAsync(GodExpMessage.ClaimOnline);
            var claimed = await ClaimedAsync(player, "online_training");
            Assert.True(claimed.GetProperty("experience").GetUInt64() > 0);
        }

        [Fact]
        public async Task OfflineTrainingPaysWhenClaimedAfterLoggingBackIn()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 640, 600, GameMaster);
            await PrayAsync(player);
            await player.Game.TrainingAsync(TrainingMessage.Query);
            Assert.Equal(900u, (await player.Game.WaitForTrainingAsync(TrainingMessage.Query))?.Data);
            await player.Game.TrainingAsync(TrainingMessage.Enter);
            var moved = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((601u, (ushort)65, (ushort)55), (moved?.Command, moved?.X, moved?.Y));
            Assert.NotNull(await player.Game.WaitForTrainingAsync(TrainingMessage.Enter));
            await this.Servers.WaitForEventAsync("game", "training.offline_started", "name", player.User.Name);

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name, 601, 65);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET OfflineTrainingSince = @since WHERE CharacterID = @id",
                ("@since", DateTime.UtcNow.AddMinutes(-150.5)), ("@id", player.User.Id));

            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.NotNull(await again.Game.WaitForTrainingAsync(TrainingMessage.AskResult));
            // The ground's portal doesn't let a trainee leave without the result
            await again.Game.UsePortalAsync(again.User.Id, 82, 59);
            var stayed = await again.Game.WaitForActionAsync(ActionMessage.Teleport, again.User.Id);
            Assert.Equal((601u, (ushort)65, (ushort)55), (stayed?.Command, stayed?.X, stayed?.Y));
            Assert.NotNull(await again.Game.WaitForTrainingInfoAsync());

            await again.Game.TrainingAsync(TrainingMessage.Result);
            var result = await again.Game.WaitForTrainingInfoAsync();
            Assert.Equal(((ushort)150, (ushort)750), (result?.Trained, result?.Left));
            Assert.True(result.Level > 1);

            await again.Game.TrainingAsync(TrainingMessage.Claim);
            var back = await again.Game.WaitForActionAsync(ActionMessage.Teleport, again.User.Id);
            Assert.Equal((1002u, (ushort)640, (ushort)600), (back?.Command, back?.X, back?.Y));
            var claimed = await this.Servers.WaitForEventAsync("game", "training.offline_claimed", "name", again.User.Name);
            Assert.Equal(150, claimed.GetProperty("minutes").GetInt32());
            Assert.NotNull(await again.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Level, out var level) && level == result.Level));
        }
    
        [Fact]
        public async Task TheTrainingGroundsPortalTakesOthersToTwinCity()
        {
            await using var player = (await this.PlayingAtAsync(601, 82, 59)).Player;
            await player.Game.UsePortalAsync(player.User.Id, 82, 59);
            var moved = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((1002u, (ushort)428, (ushort)378), (moved?.Command, moved?.X, moved?.Y));
        }
    }
}

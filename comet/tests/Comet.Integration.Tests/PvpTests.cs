namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Players fighting players, on 1002 away from the test monsters
    public class PvpTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint JailMap = 6000;

        public PvpTests(ServerFixture servers) : base(servers) { }

        private static Task CommandAsync(Player player, string command) =>
            player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", command);

        private static async Task FreePkAsync(Player player)
        {
            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.PkMode, command: 0));
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.PkMode, player.User.Id));
        }

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player) =>
            this.Servers.WaitForEventAsync("game", "combat.rejected", "name", player.User.Name);

        [Fact]
        public async Task CaptureModeOnlyAttacksFlashingPlayers()
        {
            await using var attacker = (await this.PlayingAtAsync(1002, 700, 700)).Player;
            await using var victim = (await this.PlayingAtAsync(1002, 701, 700, GameMaster)).Player;

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 701, 700);
            Assert.Equal("pk_mode", (await RejectedAsync(attacker)).GetProperty("reason").GetString());

            await CommandAsync(victim, "/flash 30");
            Assert.NotNull(await attacker.Game.WaitForAttribAsync(a =>
                a.Id == victim.User.Id && a.Values.TryGetValue(Status, out var s) && (s & Flashing) != 0));
            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 701, 700);
            var hit = await victim.Game.WaitForInteractAsync(i => i.Sender == attacker.User.Id && i.Target == victim.User.Id);
            Assert.Equal(InteractMessage.Attack, hit?.Action);
            Assert.NotNull(await attacker.Game.WaitForInteractAsync(i => i.Sender == attacker.User.Id && i.Target == victim.User.Id));
            if (hit.Data > 0)
                Assert.NotNull(await victim.Game.WaitForAttribAsync(a =>
                    a.Values.TryGetValue(Health, out var hp) && hp == victim.User.HealthPoints - hit.Data));
        }

        [Fact]
        public async Task KillingAnInnocentPlayerFlashesAndCostsPkPoints()
        {
            await using var attacker = (await this.PlayingAtAsync(1002, 720, 700, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(1002, 721, 700)).Player;
            await FreePkAsync(attacker);

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 721, 700);
            Assert.NotNull(await attacker.Game.WaitForAttribAsync(a =>
                a.Id == attacker.User.Id && a.Values.TryGetValue(Status, out var s) && (s & Flashing) != 0));
            var kill = await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(attacker.User.Id, kill?.Sender);
            Assert.NotNull(await attacker.Game.WaitForAttribAsync(a =>
                a.Id == attacker.User.Id && a.Values.TryGetValue(PkPoints, out var p) && p == 10));

            var died = await this.Servers.WaitForEventAsync("game", "character.died", "name", victim.User.Name);
            Assert.Equal(attacker.User.Id, died.GetProperty("killerId").GetUInt32());
            var pk = await this.Servers.WaitForEventAsync("game", "pk.kill", "name", attacker.User.Name);
            Assert.Equal("crime", pk.GetProperty("crime").GetString());
        }

        [Fact]
        public async Task GuardsKillFlashingPlayersAndJailBlackNames()
        {
            var at = ServerFixture.GuardOnDuty;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 3, at.Y + 3, GameMaster)).Player;
            var guard = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(guard);

            // An innocent player is left alone
            Assert.Null(await player.Game.WaitForInteractAsync(i => i.Target == player.User.Id, TimeSpan.FromSeconds(2)));

            await CommandAsync(player, "/pk 150");
            await CommandAsync(player, "/flash 30");
            var kill = await player.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == player.User.Id, TimeSpan.FromSeconds(10));
            Assert.Equal(guard.Id, kill?.Sender);
            var jailed = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal(JailMap, jailed?.Command);
            var logged = await this.Servers.WaitForEventAsync("game", "pk.jailed", "name", player.User.Name);
            Assert.Equal("killed_by_guard", logged.GetProperty("reason").GetString());
        }
    }
}

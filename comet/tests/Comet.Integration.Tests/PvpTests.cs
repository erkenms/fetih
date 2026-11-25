namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Players fighting players, in Desert City unless a test is about the map
    public class PvpTests : IntegrationTest
    {
        private const uint JailMap = 6000;
        private const uint DesertCity = ServerFixture.DesertCity, PkField = ServerFixture.PkField;

        public PvpTests(ServerFixture servers) : base(servers) { }

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player) =>
            this.Servers.WaitForEventAsync("game", "combat.rejected", "name", player.User.Name);

        [Fact]
        public async Task CaptureModeOnlyAttacksFlashingPlayers()
        {
            await using var attacker = (await this.PlayingAtAsync(DesertCity, 100, 100)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 101, 100, GameMaster)).Player;
            await using var watcher = (await this.PlayingAtAsync(DesertCity, 102, 100)).Player;

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 101, 100);
            Assert.Equal("pk_mode", (await RejectedAsync(attacker)).GetProperty("reason").GetString());

            await CommandAsync(victim, "/flash 30");
            Assert.NotNull(await attacker.Game.WaitForAttribAsync(a =>
                a.Id == victim.User.Id && a.Values.TryGetValue(Status, out var s) && (s & Flashing) != 0));
            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 101, 100);
            // With no dodge every swing hits, for at least 1
            var hit = await victim.Game.WaitForInteractAsync(i => i.Sender == attacker.User.Id && i.Target == victim.User.Id);
            Assert.Equal(InteractMessage.Attack, hit?.Action);
            Assert.True(hit.Data > 0);
            var turn = await watcher.Game.WaitForActionAsync(ActionMessage.Turn, victim.User.Id);
            Assert.Equal((ushort)2, turn?.Direction);
            // The turn goes out before the hit, and not to the one turning
            Assert.Null(await victim.Game.WaitForActionAsync(ActionMessage.Turn, victim.User.Id, Already));
            Assert.NotNull(await attacker.Game.WaitForInteractAsync(i => i.Sender == attacker.User.Id && i.Target == victim.User.Id));
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Health, out var hp) && hp == victim.User.HealthPoints - hit.Data));
        }

        [Fact]
        public async Task KillingAnInnocentPlayerFlashesAndCostsPkPoints()
        {
            await using var attacker = (await this.PlayingAtAsync(DesertCity, 120, 100, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 121, 100)).Player;
            await FreePkAsync(attacker);

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 121, 100);
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
        public async Task TwinCityForbidsAttackingPlayers()
        {
            await using var attacker = (await this.PlayingAtAsync(1002, 700, 700)).Player;
            await using var victim = (await this.PlayingAtAsync(1002, 701, 700)).Player;
            await FreePkAsync(attacker);

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 701, 700);
            Assert.Equal("peaceful_map", (await RejectedAsync(attacker)).GetProperty("reason").GetString());
            // A hit would have been queued for the victim before the refusal was logged
            await SyncAsync(victim);
            Assert.Null(await victim.Game.WaitForInteractAsync(i => i.Target == victim.User.Id, Already));
        }

        [Fact]
        public async Task KillingOnAPkFieldIsNoCrime()
        {
            await using var attacker = (await this.PlayingAtAsync(PkField, 50, 50, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(PkField, 51, 50)).Player;
            await FreePkAsync(attacker);

            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, 51, 50);
            var kill = await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(attacker.User.Id, kill?.Sender);
            var pk = await this.Servers.WaitForEventAsync("game", "pk.kill", "name", attacker.User.Name);
            Assert.Equal("no_crime", pk.GetProperty("crime").GetString());
            await SyncAsync(attacker);
            Assert.Null(await attacker.Game.WaitForAttribAsync(a =>
                a.Id == attacker.User.Id && a.Values.TryGetValue(Status, out var s) && (s & Flashing) != 0, Already));
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

            // The guard goes back to its post
            await using var watcher = (await this.PlayingAtAsync(1002, at.X + 6, at.Y)).Player;
            var seen = await watcher.Game.WaitForMonsterAsync(m => m.Id == guard.Id);
            Assert.NotNull(seen);
            var (x, y) = ((int)seen.X, (int)seen.Y);
            while ((x, y) != (at.X, at.Y))
            {
                var step = await watcher.Game.WaitForWalkAsync(guard.Id);
                Assert.NotNull(step);
                x += DeltaX[step.Direction % 8];
                y += DeltaY[step.Direction % 8];
            }
        }

        private static readonly int[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        private static readonly int[] DeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };
    }
}

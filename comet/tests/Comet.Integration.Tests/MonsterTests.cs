namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // The test spawns from ServerFixture: pheasants and guards on 1002 past (800, 800)
    public class MonsterTests : IntegrationTest
    {

        public MonsterTests(ServerFixture servers) : base(servers) { }

        private static Task<PlayerSpawn> MonsterAtAsync(Player player, (ushort X, ushort Y) at) =>
            player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);

        [Fact]
        public async Task MonstersInViewAreShownWithTheirHealthAndLevel()
        {
            var at = ServerFixture.PheasantToKill;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 5, at.Y)).Player;
            var pheasant = await MonsterAtAsync(player, at);
            Assert.NotNull(pheasant);
            Assert.Equal(("Pheasant", 104u, 33u, (ushort)1), (pheasant.Name, pheasant.Mesh, pheasant.MonsterHealth, pheasant.MonsterLevel));
        }

        [Fact]
        public async Task KillingAMonsterGivesExperienceAndItComesBack()
        {
            var at = ServerFixture.PheasantToKill;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, level: 100)).Player;
            var pheasant = await MonsterAtAsync(player, at);
            Assert.NotNull(pheasant);

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            var hit = await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Attack && i.Sender == player.User.Id);
            Assert.Equal(pheasant.Id, hit?.Target);
            Assert.True(hit.Data >= 33);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.ContainsKey(Experience)));
            var killed = await this.Servers.WaitForEventAsync("game", "monster.killed", "name", player.User.Name);
            Assert.Equal(pheasant.Id, killed.GetProperty("monsterId").GetUInt32());

            Assert.True(await player.Game.WaitForRemovalAsync(pheasant.Id, TimeSpan.FromSeconds(6)));
            var back = await player.Game.WaitForMonsterAsync(m => m.Id == pheasant.Id, TimeSpan.FromSeconds(6));
            Assert.Equal(33u, back?.MonsterHealth);
        }

        [Fact]
        public async Task ShootingTakesAnArrowAShotUntilTheQuiverIsEmpty()
        {
            var at = ServerFixture.PheasantToShoot;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 2, at.Y, GameMaster)).Player;
            var pheasant = await MonsterAtAsync(player, at);
            await CommandAsync(player, "/item " + ServerFixture.Bow.Id);
            var bow = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Bow.Id);
            await CommandAsync(player, "/item " + ServerFixture.Arrows.Id);
            var arrows = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Arrows.Id);
            await player.Game.EquipAsync(bow.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bow.Id));
            await player.Game.EquipAsync(arrows.Id, 5);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, arrows.Id));

            await player.Game.ShootAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Shoot && i.Target == pheasant.Id));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == arrows.Id && i.Amount == 2));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == arrows.Id && i.Amount == 1));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, arrows.Id));
            var stopped = await this.Servers.WaitForEventAsync("game", "combat.rejected", "name", player.User.Name);
            Assert.Equal("no_arrows", stopped.GetProperty("reason").GetString());
            Assert.Null(await this.Servers.QueryGameScalarAsync("SELECT ItemID FROM item WHERE ItemID = @id", ("@id", arrows.Id)));
        }

        [Fact]
        public async Task MonstersAttackPlayersNextToThem()
        {
            var at = ServerFixture.PheasantThatBites;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y)).Player;
            var pheasant = await MonsterAtAsync(player, at);
            var bite = await player.Game.WaitForInteractAsync(i => i.Sender == pheasant.Id && i.Target == player.User.Id);
            Assert.Equal(InteractMessage.Attack, bite?.Action);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Health, out var hp) && hp < player.User.HealthPoints));
        }

        [Fact]
        public async Task CastingMonstersHitPlayersWithTheirSpell()
        {
            var at = ServerFixture.FireDevilThatCasts;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 5, at.Y, level: 100)).Player;
            var devil = await MonsterAtAsync(player, at);
            Assert.Equal("FireDevil", devil?.Name);

            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == devil.Id, TimeSpan.FromSeconds(10));
            Assert.NotNull(effect);
            Assert.Equal(ServerFixture.Thunder.Type, effect.Type);
            var hit = Assert.Single(effect.Targets);
            Assert.Equal(player.User.Id, hit.Id);
            Assert.True(hit.Damage > 0);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Health, out var hp) && hp < player.User.HealthPoints));
        }

        [Fact]
        public async Task GuardsKillMonstersNearThem()
        {
            var guardAt = ServerFixture.GuardOnDuty;
            await using var player = (await this.PlayingAtAsync(1002, guardAt.X - 10, guardAt.Y)).Player;
            var guard = await MonsterAtAsync(player, guardAt);
            Assert.Equal("Guard1", guard?.Name);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == guard.Id, TimeSpan.FromSeconds(10));
            Assert.NotNull(effect);
            Assert.Equal(ServerFixture.GuardsSpell.Type, effect.Type);
            var kill = await player.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Sender == guard.Id, TimeSpan.FromSeconds(10));
            Assert.NotNull(kill);
        }

        [Fact]
        public async Task PlayersWhoHitAGuardDieAndCanReviveInTown()
        {
            var at = ServerFixture.GuardToProvoke;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y)).Player;
            await using var watcher = (await this.PlayingAtAsync(1002, at.X + 6, at.Y)).Player;
            var guard = await MonsterAtAsync(player, at);
            Assert.NotNull(await player.Game.WaitForSpawnAsync(watcher.User.Id));

            // Guards can only be attacked in free PK mode, and hitting one makes the player flash
            await player.Game.AttackAsync(player.User.Id, guard.Id, at.X, at.Y);
            var refused = await this.Servers.WaitForEventAsync("game", "combat.rejected", "name", player.User.Name);
            Assert.Equal("guard_not_free_pk", refused.GetProperty("reason").GetString());
            await FreePkAsync(player);
            await player.Game.AttackAsync(player.User.Id, guard.Id, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & Flashing) != 0));

            var kill = await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == player.User.Id);
            Assert.Equal(guard.Id, kill?.Sender);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & DeadStatus) != 0));
            await player.Game.ReviveAsync(player.User.Id);
            var early = await this.Servers.WaitForEventAsync("game", "character.revive_rejected", "name", player.User.Name);
            Assert.Equal("too_soon", early.GetProperty("reason").GetString());

            // The body can't move until it's a ghost; a ghost walks but can't jump
            await player.Game.WalkAsync(player.User.Id, 0);
            var lying = await this.Servers.WaitForEventAsync("game", "move.rejected", "name", player.User.Name);
            Assert.Equal("dead", lying.GetProperty("reason").GetString());
            var snapped = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((at.X + 1, (int)at.Y), (snapped.X, snapped.Y));
            Assert.NotNull(await player.Game.WaitForSpawnAsync(watcher.User.Id));
            Assert.NotNull(await player.Game.WaitForMonsterAsync(m => m.Id == guard.Id));

            await player.Game.BecomeGhostAsync(player.User.Id);

            var ghost = await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Mesh, out var m) && m >= 10_000_000);
            Assert.Equal(98u, (uint)(ghost.Values[Mesh] / 10_000_000));
            await player.Game.JumpAsync(player.User.Id, at.X + 1, at.Y, at.X + 5, at.Y + 5);
            var jump = await this.Servers.WaitForEventAsync("game", "move.rejected",
                e => e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("kind").GetString() == "jump");
            Assert.Equal("ghost", jump.GetProperty("reason").GetString());
            snapped = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((at.X + 1, (int)at.Y), (snapped.X, snapped.Y));
            await player.Game.WalkAsync(player.User.Id, 0);
            await this.Servers.WaitForEventAsync("game", "move.walk", "name", player.User.Name);
            var died = await this.Servers.WaitForEventAsync("game", "character.died", "name", player.User.Name);
            Assert.Equal(guard.Id, died.GetProperty("killerId").GetUInt32());

            await Task.Delay(TimeSpan.FromSeconds(ServerFixture.ReviveSeconds));
            await player.Game.ReviveAsync(player.User.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Health, out var hp) && hp == player.User.HealthPoints));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & DeadStatus) == 0));
            Assert.Equal((ushort)1050, await player.Game.WaitForMagicEffectAsync(player.User.Id));
            var teleport = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((1002u, (ushort)430, (ushort)380), (teleport?.Command, teleport?.X, teleport?.Y));
            await this.Servers.WaitForEventAsync("game", "character.revived", "name", player.User.Name);
        }
    }
}

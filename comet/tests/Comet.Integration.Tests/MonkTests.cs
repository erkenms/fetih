namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // The monk's skills, with the test magictype.dat of ServerFixture
    public class MonkTests : IntegrationTest
    {
        public MonkTests(ServerFixture servers) : base(servers) { }

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player) =>
            this.Servers.WaitForEventAsync("game", "magic.rejected", "name", player.User.Name);

        private async Task HoldBeadsAsync(Player player)
        {
            var beads = await GiveAsync(player, ServerFixture.Beads.Id);
            await player.Game.EquipAsync(beads.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, beads.Id));
        }

        private async Task LearnAsync(Player player, ushort magicType)
        {
            await CommandAsync(player, $"/learn {magicType}");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == magicType));
        }

        private static async Task TeamUpAsync(Player leader, Player member)
        {
            await leader.Game.TeamAsync(TeamMessage.Create, leader.User.Id);
            Assert.NotNull(await leader.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Create && t.Data == leader.User.Id));
            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id));
            await leader.Game.TeamAsync(TeamMessage.Invite, member.User.Id);
            Assert.NotNull(await member.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Invite && t.Data == leader.User.Id));
            await member.Game.TeamAsync(TeamMessage.AcceptInvite, leader.User.Id);
            Assert.NotNull(await leader.Game.WaitForTeamMemberAsync(m => m.Id == member.User.Id));
        }

        [Fact]
        public async Task RadiantPalmNeedsBeadsAndHitsFourPacesInARowForStamina()
        {
            var at = ServerFixture.PheasantForPalm;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 4, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await LearnAsync(player, 10381);

            await player.Game.CastAsync(player.User.Id, 10381, 0, at.X, at.Y);
            Assert.Equal("wrong_weapon", (await RejectedAsync(player)).GetProperty("reason").GetString());

            await HoldBeadsAsync(player);
            await player.Game.CastAsync(player.User.Id, 10381, 0, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 10381);
            Assert.Contains(effect.Targets, t => t.Id == pheasant.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Stamina, out var s) && s == 80));
        }

        [Fact]
        public async Task WhirlwindKickHitsAroundTheCasterWithinThreePaces()
        {
            var at = ServerFixture.PheasantForKick;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 3, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await LearnAsync(player, 10415);
            await HoldBeadsAsync(player);

            await player.Game.CastAsync(player.User.Id, 10415, 0, (ushort)(at.X + 3), at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 10415);
            Assert.Contains(effect.Targets, t => t.Id == pheasant.Id);
            // The client plays the kick as X hits
            Assert.Equal((6, 0), (effect.X, effect.Y));
        }

        [Fact]
        public async Task TripleAttackGoesOffInPlaceOfASwingAndHitsThreeTimes()
        {
            var at = ServerFixture.MacaqueForTripleAttack;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var macaque = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(macaque);
            await LearnAsync(player, 10490);

            await player.Game.CastAsync(player.User.Id, 10490, macaque.Id, at.X, at.Y);
            Assert.Equal("passive", (await RejectedAsync(player)).GetProperty("reason").GetString());

            await HoldBeadsAsync(player);
            await player.Game.AttackAsync(player.User.Id, macaque.Id, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 10490);
            Assert.Equal(3, effect.Targets.Count);
            Assert.All(effect.Targets, t => Assert.Equal(macaque.Id, t.Id));
        }

        [Fact]
        public async Task TripleAttackEndsWithTheHitThatKills()
        {
            var at = ServerFixture.PheasantForTripleAttack;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await LearnAsync(player, 10490);
            await HoldBeadsAsync(player);

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 10490);
            Assert.Equal(pheasant.Id, Assert.Single(effect.Targets).Id);
        }

        private const ulong TyrantAura = 1UL << (98 - 64), FendAura = 1UL << (100 - 64);

        [Fact]
        public async Task AMonkKeepsOneAuraUpOnTheTeamAndCastingItAgainTakesItDown()
        {
            await using var monk = (await this.PlayingAtAsync(1002, 920, 760, GameMaster, level: 100)).Player;
            await using var mate = (await this.PlayingAtAsync(1002, 922, 760, GameMaster, level: 100)).Player;
            await LearnAsync(monk, 10395);
            await LearnAsync(monk, 10410);
            await TeamUpAsync(monk, mate);

            await monk.Game.CastAsync(monk.User.Id, 10395, 0, 920, 760);
            var effect = await monk.Game.WaitForEffectAsync(e => e.Caster == monk.User.Id && e.Type == 10395);
            Assert.Equal(new[] { monk.User.Id, mate.User.Id }, effect.Targets.Select(t => t.Id).OrderBy(id => id != monk.User.Id));
            Assert.NotNull(await mate.Game.WaitForAttribAsync(a => a.Id == mate.User.Id && (a.StatusHigh & TyrantAura) != 0));

            await monk.Game.CastAsync(monk.User.Id, 10410, 0, 920, 760);
            Assert.NotNull(await monk.Game.WaitForAttribAsync(a => a.Id == monk.User.Id &&
                (a.StatusHigh & FendAura) != 0 && (a.StatusHigh & TyrantAura) == 0));

            await monk.Game.CastAsync(monk.User.Id, 10410, 0, 920, 760);
            Assert.NotNull(await mate.Game.WaitForAttribAsync(a => a.Id == mate.User.Id &&
                a.Values.ContainsKey(Status) && (a.StatusHigh & FendAura) == 0));
        }

        private const ulong SoulShackle = 1UL << (111 - 64);

        [Fact]
        public async Task SoulShackleKeepsTheDeadFromReviving()
        {
            await using var monk = (await this.PlayingAtAsync(ServerFixture.PkField, 60, 60, GameMaster, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 61, 60)).Player;
            await LearnAsync(monk, 10405);
            await FreePkAsync(monk);

            await monk.Game.CastAsync(monk.User.Id, 10405, victim.User.Id, 61, 60);
            Assert.Equal("alive_target", (await RejectedAsync(monk)).GetProperty("reason").GetString());

            await monk.Game.AttackAsync(monk.User.Id, victim.User.Id, 61, 60);
            Assert.NotNull(await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, System.TimeSpan.FromSeconds(15)));
            await monk.Game.CastAsync(monk.User.Id, 10405, victim.User.Id, 61, 60);
            Assert.NotNull(await monk.Game.WaitForEffectAsync(e => e.Caster == monk.User.Id && e.Type == 10405));
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a => a.Id == victim.User.Id && (a.StatusHigh & SoulShackle) != 0));

            await Task.Delay(System.TimeSpan.FromSeconds(ServerFixture.ReviveSeconds));
            await victim.Game.ReviveAsync(victim.User.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "character.revive_rejected", "name", victim.User.Name);
            Assert.Equal("shackled", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task TranquilityFreesAShackledSoul()
        {
            await using var monk = (await this.PlayingAtAsync(ServerFixture.PkField, 60, 70, GameMaster, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 61, 70)).Player;
            await LearnAsync(monk, 10405);
            await LearnAsync(monk, 10425);
            await FreePkAsync(monk);
            await monk.Game.AttackAsync(monk.User.Id, victim.User.Id, 61, 70);
            Assert.NotNull(await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, System.TimeSpan.FromSeconds(15)));
            await monk.Game.CastAsync(monk.User.Id, 10405, victim.User.Id, 61, 70);
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a => a.Id == victim.User.Id && (a.StatusHigh & SoulShackle) != 0));

            await Task.Delay(Casting);
            await monk.Game.CastAsync(monk.User.Id, 10425, victim.User.Id, 61, 70);
            var effect = await monk.Game.WaitForEffectAsync(e => e.Caster == monk.User.Id && e.Type == 10425);
            Assert.Equal(victim.User.Id, Assert.Single(effect.Targets).Id);
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a => a.Id == victim.User.Id &&
                a.Values.ContainsKey(Status) && (a.StatusHigh & SoulShackle) == 0));
        }

        // The server's least time between casts
        private static readonly System.TimeSpan Casting = System.TimeSpan.FromMilliseconds(600);

        [Fact]
        public async Task CompassionReachesTheTeamAndDispelsWaitThreeSeconds()
        {
            await using var monk = (await this.PlayingAtAsync(1002, 920, 790, GameMaster, level: 100)).Player;
            await using var mate = (await this.PlayingAtAsync(1002, 925, 790, GameMaster, level: 100)).Player;
            await LearnAsync(monk, 10430);
            await TeamUpAsync(monk, mate);

            await monk.Game.CastAsync(monk.User.Id, 10430, 0, 920, 790);
            var effect = await monk.Game.WaitForEffectAsync(e => e.Caster == monk.User.Id && e.Type == 10430);
            Assert.Equal(new[] { monk.User.Id, mate.User.Id }, effect.Targets.Select(t => t.Id).OrderBy(id => id != monk.User.Id));

            await Task.Delay(Casting);
            await monk.Game.CastAsync(monk.User.Id, 10430, 0, 920, 790);
            Assert.Equal("too_soon", (await RejectedAsync(monk)).GetProperty("reason").GetString());
        }

        private const ulong OblivionStatus = 1UL << (112 - 64);

        [Fact]
        public async Task OblivionCountsItsKillsOnTheBeads()
        {
            var at = ServerFixture.PheasantForOblivion;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100, strength: 5000)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await LearnAsync(player, 10390);
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));

            await player.Game.CastAsync(player.User.Id, 10390, 0, (ushort)(at.X + 1), at.Y);
            Assert.NotNull(await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 10390));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                (a.StatusHigh & OblivionStatus) != 0 && a.Values.TryGetValue(Status, out var s) && (s & XpFull) == 0));
            var started = await this.Servers.WaitForEventAsync("game", "status.started", "name", player.User.Name);
            Assert.Equal(30, started.GetProperty("seconds").GetInt32());

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            var kill = await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id);
            Assert.Equal((1u << 16) | 1, kill?.Data);
        }
    }
}

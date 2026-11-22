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
        }

        [Fact]
        public async Task TripleAttackGoesOffInPlaceOfASwingAndHitsThreeTimes()
        {
            var at = ServerFixture.PheasantForTripleAttack;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await LearnAsync(player, 10490);

            await player.Game.CastAsync(player.User.Id, 10490, pheasant.Id, at.X, at.Y);
            Assert.Equal("passive", (await RejectedAsync(player)).GetProperty("reason").GetString());

            await HoldBeadsAsync(player);
            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 10490);
            Assert.Equal(3, effect.Targets.Count);
            Assert.All(effect.Targets, t => Assert.Equal(pheasant.Id, t.Id));
        }

        private const ulong TyrantAura = 1UL << (98 - 64), FendAura = 1UL << (100 - 64);

        [Fact]
        public async Task AMonkKeepsOneAuraUpOnTheTeamAndCastingItAgainTakesItDown()
        {
            await using var monk = (await this.PlayingAtAsync(1002, 740, 900, GameMaster, level: 100)).Player;
            await using var mate = (await this.PlayingAtAsync(1002, 742, 900, GameMaster, level: 100)).Player;
            await LearnAsync(monk, 10395);
            await LearnAsync(monk, 10410);
            await TeamUpAsync(monk, mate);

            await monk.Game.CastAsync(monk.User.Id, 10395, 0, 740, 900);
            var effect = await monk.Game.WaitForEffectAsync(e => e.Caster == monk.User.Id && e.Type == 10395);
            Assert.Equal(new[] { monk.User.Id, mate.User.Id }, effect.Targets.Select(t => t.Id).OrderBy(id => id != monk.User.Id));
            Assert.NotNull(await mate.Game.WaitForAttribAsync(a => a.Id == mate.User.Id && (a.StatusHigh & TyrantAura) != 0));

            await monk.Game.CastAsync(monk.User.Id, 10410, 0, 740, 900);
            Assert.NotNull(await monk.Game.WaitForAttribAsync(a => a.Id == monk.User.Id &&
                (a.StatusHigh & FendAura) != 0 && (a.StatusHigh & TyrantAura) == 0));

            await monk.Game.CastAsync(monk.User.Id, 10410, 0, 740, 900);
            Assert.NotNull(await mate.Game.WaitForAttribAsync(a => a.Id == mate.User.Id &&
                a.Values.ContainsKey(Status) && (a.StatusHigh & FendAura) == 0));
        }
    }
}

namespace Comet.Integration.Tests
{
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
    }
}

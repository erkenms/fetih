namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Spells, weapon proficiency and the XP bar, with the test magictype.dat of ServerFixture
    public class MagicTests : IntegrationTest
    {
        public MagicTests(ServerFixture servers) : base(servers) { }

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player) =>
            this.Servers.WaitForEventAsync("game", "magic.rejected", "name", player.User.Name);

        [Fact]
        public async Task SpellsAndProficienciesAreListedAtLogin()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 700, 760, GameMaster);
            await using (player)
            {
                Assert.Empty(player.Game.Spells);
                await CommandAsync(player, "/learn 1000 1");
                Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1000 && m.Level == 1));
                await CommandAsync(player, "/prof 410 2");
                Assert.NotNull(await player.Game.WaitForWeaponSkillAsync(w => w.Type == 410 && w.Level == 2 && w.Needed == 1000));
                await this.LogOutAsync(player);
            }

            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(new MagicInfo(1000, 1, 0), Assert.Single(again.Game.Spells));
            Assert.Equal(new WeaponSkill(410, 2, 0, 1000), Assert.Single(again.Game.Proficiencies));
        }

        [Fact]
        public async Task ThunderKillsAMonsterAndItsExperienceLevelsTheSpell()
        {
            var at = ServerFixture.PheasantForMagic;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 5, at.Y, GameMaster)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);

            await player.Game.CastAsync(player.User.Id, 1000, pheasant.Id, at.X, at.Y);
            Assert.Equal("not_learned", (await RejectedAsync(player)).GetProperty("reason").GetString());

            await CommandAsync(player, "/learn 1000");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1000 && m.Level == 0));
            await player.Game.CastAsync(player.User.Id, 1000, pheasant.Id, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id);
            Assert.Equal((1000, at.X, at.Y), ((int)effect?.Type, effect.X, effect.Y));
            Assert.Equal(pheasant.Id, Assert.Single(effect.Targets).Id);
            Assert.True(effect.Targets[0].Damage >= 1000);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1000 && m.Level == 1));
            var cast = await this.Servers.WaitForEventAsync("game", "magic.cast", "name", player.User.Name);
            Assert.Equal(1, cast.GetProperty("targetCount").GetInt32());
        }

        [Fact]
        public async Task SupermanNeedsAFullXpBarAndEmptiesIt()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 780, GameMaster)).Player;
            await CommandAsync(player, "/learn 1025");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1025));

            await player.Game.CastAsync(player.User.Id, 1025, 0, 700, 780);
            Assert.Equal("xp_not_full", (await RejectedAsync(player)).GetProperty("reason").GetString());

            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));
            await player.Game.CastAsync(player.User.Id, 1025, 0, 700, 780);
            Assert.NotNull(await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 1025));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & Superman) != 0 && (s & XpFull) == 0));
            var started = await this.Servers.WaitForEventAsync("game", "status.started", "name", player.User.Name);
            Assert.Equal(20, started.GetProperty("seconds").GetInt32());
        }

        [Fact]
        public async Task FastBladeNeedsABladeAndHitsAlongTheLineForStamina()
        {
            var at = ServerFixture.PheasantForFastBlade;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 3, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(player, "/learn 1045");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1045));

            // Aimed past the pheasant, as the client sends a line spell: a cell, no target
            await player.Game.CastAsync(player.User.Id, 1045, 0, at.X - 2, at.Y);
            Assert.Equal("wrong_weapon", (await RejectedAsync(player)).GetProperty("reason").GetString());

            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(blade.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id));

            await player.Game.CastAsync(player.User.Id, 1045, 0, at.X - 2, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 1045);
            Assert.Contains(effect.Targets, t => t.Id == pheasant.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Stamina, out var s) && s == 80));
        }

        [Fact]
        public async Task AttrSetsAddsAndTakesAwayStats()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 820, GameMaster)).Player;
            await CommandAsync(player, "/attr spi 4");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Spirit, out var s) && s == 4));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(MaxMana, out var m) && m == 20));
            await CommandAsync(player, "/attr spi +2");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Spirit, out var s) && s == 6));
            await CommandAsync(player, "/attr spi -10");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Spirit, out var s) && s == 0));
            await CommandAsync(player, "/attr points 7");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(AttributePoints, out var p) && p == 7));
        }

        [Fact]
        public async Task SpiritGivesManaThatRestoreFills()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 800, GameMaster)).Player;
            await CommandAsync(player, "/level 2");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(AttributePoints, out var p) && p == 3));
            await player.Game.AllotAsync(0, 0, 0, 3);
            ulong mana = (player.User.Spirit + 3UL) * 5;
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(MaxMana, out var m) && m == mana));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Mana, out var m) && m == mana));

            await CommandAsync(player, "/restore");
            foreach (var type in new[] { Health, Mana, Stamina })
                Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.Count == 1 && a.Values.ContainsKey(type)
                    && (type != Mana || a.Values[Mana] == mana)));
        }

        [Fact]
        public async Task HittingMonstersTrainsTheWeapon()
        {
            var at = ServerFixture.PheasantForBlade;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(blade.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id));

            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            var proficiency = await player.Game.WaitForWeaponSkillAsync(w => w.Type == ServerFixture.Blade.Id / 1000);
            Assert.Equal((1u, 100u), (proficiency?.Level, proficiency.Needed));
            Assert.True(proficiency.Experience > 0);
        }
    }
}

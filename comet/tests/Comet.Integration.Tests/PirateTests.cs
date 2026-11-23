namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // The pirates' skills, with the test magictype.dat of ServerFixture
    public class PirateTests : IntegrationTest
    {
        public PirateTests(ServerFixture servers) : base(servers) { }

        private const uint DesertCity = ServerFixture.DesertCity;

        // Status bits from 64, as MsgUserAttrib 25's second word has them
        private const ulong Fatigue = 1UL << (119 - 64), Cannon = 1UL << (120 - 64), Blackbeard = 1UL << (121 - 64);

        private static async Task LearnAsync(Player player, ushort magicType)
        {
            await CommandAsync(player, $"/learn {magicType}");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == magicType));
        }

        [Fact]
        public async Task ScurvyBombTiresWhoItHitsSoTheirSkillsCostMoreStamina()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 140, 100, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 144, 100, GameMaster)).Player;
            await FreePkAsync(pirate);
            await LearnAsync(pirate, 11040);
            await LearnAsync(victim, 11040);

            await pirate.Game.CastAsync(pirate.User.Id, 11040, 0, 145, 100);
            var effect = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11040);
            Assert.Equal(victim.User.Id, Assert.Single(effect.Targets).Id);
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a => a.Id == victim.User.Id &&
                a.Values.ContainsKey(Status) && (a.StatusHigh & Fatigue) != 0));

            // Aimed where no one stands, 20 stamina and the fatigue's 5
            await victim.Game.CastAsync(victim.User.Id, 11040, 0, 144, 110);
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Stamina, out var s) && s == 75));
        }
    

        [Fact]
        public async Task GaleBombHitsTheThreeNearestTheCell()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 160, 100, GameMaster)).Player;
            await using var first = (await this.PlayingAtAsync(DesertCity, 165, 100)).Player;
            await using var second = (await this.PlayingAtAsync(DesertCity, 166, 100)).Player;
            await using var third = (await this.PlayingAtAsync(DesertCity, 167, 100)).Player;
            await using var fourth = (await this.PlayingAtAsync(DesertCity, 168, 101)).Player;
            await FreePkAsync(pirate);
            await LearnAsync(pirate, 11070);

            await pirate.Game.CastAsync(pirate.User.Id, 11070, 0, 166, 100);
            var effect = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11070);
            Assert.Equal(new[] { first.User.Id, second.User.Id, third.User.Id }.Order(),
                effect.Targets.Select(t => t.Id).Order());
        }

        [Fact]
        public async Task BladeTempestDashesToTheCellHittingWhoIsOnTheWay()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 180, 100, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 184, 100)).Player;
            await FreePkAsync(pirate);
            await LearnAsync(pirate, 11110);

            await pirate.Game.CastAsync(pirate.User.Id, 11110, 0, 190, 100);
            Assert.Equal("wrong_weapon", (await this.Servers.WaitForEventAsync("game", "magic.rejected", "name",
                pirate.User.Name)).GetProperty("reason").GetString());
            var rapier = await GiveAsync(pirate, ServerFixture.Rapier.Id);
            await pirate.Game.EquipAsync(rapier.Id, 4);
            Assert.NotNull(await pirate.Game.WaitForItemActionAsync(ItemMessage.Equipped, rapier.Id));

            await pirate.Game.CastAsync(pirate.User.Id, 11110, 0, 190, 100);
            var effect = await victim.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11110);
            Assert.Equal((190, 100), (effect.X, effect.Y));
            Assert.Equal(victim.User.Id, Assert.Single(effect.Targets).Id);
            Assert.Equal((DesertCity, 190, 100), await this.SavedPositionAsync(pirate));
        }
    

        private static async Task HoldAsync(Player player, uint itemType, byte slot = 4)
        {
            var weapon = await GiveAsync(player, itemType);
            await player.Game.EquipAsync(weapon.Id, slot);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, weapon.Id));
        }

        [Fact]
        public async Task EagleEyeOnWhomKrakenSpottedEndsItsCooldownByAdrenalineRush()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 200, 100, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 205, 100)).Player;
            await using var near = (await this.PlayingAtAsync(DesertCity, 207, 101)).Player;
            await FreePkAsync(pirate);
            await HoldAsync(pirate, ServerFixture.Pistol.Id);
            foreach (ushort spell in new ushort[] { 11100, 11030, 11130 })
                await LearnAsync(pirate, spell);

            await pirate.Game.CastAsync(pirate.User.Id, 11100, victim.User.Id, 205, 100);
            var kraken = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11100);
            Assert.Equal(new[] { victim.User.Id, near.User.Id }, kraken.Targets.Select(t => t.Id));

            await Task.Delay(600);
            await pirate.Game.CastAsync(pirate.User.Id, 11030, victim.User.Id, 205, 100);
            Assert.NotNull(await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11030));
            var rush = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11130);
            Assert.Equal(pirate.User.Id, Assert.Single(rush.Targets).Id);
        }

        [Fact]
        public async Task AdrenalineRushGoesOffOnTakingDamage()
        {
            var at = ServerFixture.PheasantForAdrenaline;
            await using var pirate = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster)).Player;
            await LearnAsync(pirate, 11130);
            Assert.NotNull(await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11130,
                System.TimeSpan.FromSeconds(20)));
        }

        [Fact]
        public async Task BlackSpotGoesOffOnHittingWithAPistol()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 220, 100, GameMaster, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 221, 100)).Player;
            await FreePkAsync(pirate);
            await HoldAsync(pirate, ServerFixture.Pistol.Id);
            await LearnAsync(pirate, 11120);

            await pirate.Game.AttackAsync(pirate.User.Id, victim.User.Id, 221, 100);
            // A mark over the victim, not a spell effect showing a hit of 0; off after 8 seconds
            Assert.True(await victim.Game.WaitForDeadMarkAsync(victim.User.Id, remove: false, System.TimeSpan.FromSeconds(15)));
            Assert.Null(await victim.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11120, System.TimeSpan.Zero));
            Assert.True(await victim.Game.WaitForDeadMarkAsync(victim.User.Id, remove: true, System.TimeSpan.FromSeconds(20)));
        }
    

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player) =>
            this.Servers.WaitForEventAsync("game", "magic.rejected", "name", player.User.Name);

        // The first cast, with the XP bar full, starts the status; then each cast is an attack
        private async Task StartXpStatusAsync(Player pirate, ushort magicType, ulong status)
        {
            await pirate.Game.CastAsync(pirate.User.Id, magicType, pirate.User.Id, 0, 0);
            Assert.Equal("xp_not_full", (await RejectedAsync(pirate)).GetProperty("reason").GetString());
            await CommandAsync(pirate, "/xp");
            Assert.NotNull(await pirate.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));
            await pirate.Game.CastAsync(pirate.User.Id, magicType, pirate.User.Id, 0, 0);
            var started = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == magicType);
            Assert.Equal(pirate.User.Id, Assert.Single(started.Targets).Id);
            Assert.NotNull(await pirate.Game.WaitForAttribAsync(a => a.Id == pirate.User.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & XpFull) == 0 && (a.StatusHigh & status) != 0));
            await Task.Delay(600);
        }

        [Fact]
        public async Task CannonBarrageStartsItsStatusThenFiresAroundTheCell()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 240, 100, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 245, 100)).Player;
            await using var near = (await this.PlayingAtAsync(DesertCity, 247, 102)).Player;
            await FreePkAsync(pirate);
            await HoldAsync(pirate, ServerFixture.Rapier.Id);
            await LearnAsync(pirate, 11050);
            await StartXpStatusAsync(pirate, 11050, Cannon);

            for (int shot = 0; shot < 2; shot++)
            {
                await pirate.Game.CastAsync(pirate.User.Id, 11050, victim.User.Id, 245, 100);
                var effect = await victim.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11050 &&
                    e.Targets.Any(t => t.Id == victim.User.Id));
                Assert.Equal(new[] { victim.User.Id, near.User.Id }, effect.Targets.Select(t => t.Id));
                await Task.Delay(600);
            }
        }

        [Fact]
        public async Task BlackbeardsRageHitsTheTargetAndTheNearestAroundIt()
        {
            await using var pirate = (await this.PlayingAtAsync(DesertCity, 260, 100, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(DesertCity, 265, 100)).Player;
            await using var second = (await this.PlayingAtAsync(DesertCity, 266, 100)).Player;
            await using var third = (await this.PlayingAtAsync(DesertCity, 267, 101)).Player;
            await using var fourth = (await this.PlayingAtAsync(DesertCity, 269, 102)).Player;
            await FreePkAsync(pirate);
            await HoldAsync(pirate, ServerFixture.Pistol.Id);
            await LearnAsync(pirate, 11060);
            await StartXpStatusAsync(pirate, 11060, Blackbeard);

            await pirate.Game.CastAsync(pirate.User.Id, 11060, victim.User.Id, 265, 100);
            var effect = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11060 &&
                e.Targets.Any(t => t.Id == victim.User.Id));
            Assert.Equal(new[] { victim.User.Id, second.User.Id, third.User.Id }, effect.Targets.Select(t => t.Id));
        }
    

        [Fact]
        public async Task WindstormTurnsARapierSwingIntoACombo()
        {
            var at = ServerFixture.MacaqueForWindstorm;
            await using var pirate = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var macaque = await pirate.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(macaque);
            await HoldAsync(pirate, ServerFixture.Rapier.Id);
            await LearnAsync(pirate, 11140);

            await pirate.Game.AttackAsync(pirate.User.Id, macaque.Id, at.X, at.Y);
            var combo = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11140);
            Assert.Equal(3, combo.Targets.Count);
            Assert.All(combo.Targets, t => Assert.Equal(macaque.Id, t.Id));
        }

        [Fact]
        public async Task AWindstormComboEndsWithTheHitThatKills()
        {
            var at = ServerFixture.PheasantForWindstorm;
            await using var pirate = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await pirate.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await HoldAsync(pirate, ServerFixture.Rapier.Id);
            await LearnAsync(pirate, 11140);

            await pirate.Game.AttackAsync(pirate.User.Id, pheasant.Id, at.X, at.Y);
            var combo = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11140);
            Assert.Equal(pheasant.Id, Assert.Single(combo.Targets).Id);
        }
    }
}

namespace Comet.Integration.Tests
{
    using System.Linq;
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
        public async Task ScatterTakesItsArrowsFromTheQuiverAndNeedsEnoughOfThem()
        {
            var at = ServerFixture.PheasantForScatter;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 3, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(player, "/learn 8001");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 8001));
            var bow = await GiveAsync(player, ServerFixture.Bow.Id);
            await player.Game.EquipAsync(bow.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bow.Id));

            await player.Game.CastAsync(player.User.Id, 8001, 0, at.X, at.Y);
            Assert.Equal("no_item", (await RejectedAsync(player)).GetProperty("reason").GetString());

            var arrows = await GiveAsync(player, ServerFixture.Arrows.Id);
            await player.Game.EquipAsync(arrows.Id, 5);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, arrows.Id));
            await player.Game.CastAsync(player.User.Id, 8001, 0, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 8001);
            Assert.Contains(effect.Targets, t => t.Id == pheasant.Id);
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == arrows.Id && i.Amount == ServerFixture.Arrows.Amount - 2));

            await player.Game.CastAsync(player.User.Id, 8001, 0, at.X, at.Y);
            Assert.Equal("no_item", (await RejectedAsync(player)).GetProperty("reason").GetString());
        }

        [Fact]
        public async Task AFlyingArcherIsOutOfAPheasantsReach()
        {
            var at = ServerFixture.PheasantUnderFlyer;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100)).Player;
            var bow = await GiveAsync(player, ServerFixture.Bow.Id);
            await player.Game.EquipAsync(bow.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bow.Id));
            await CommandAsync(player, "/learn 8002");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 8002));
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));

            await player.Game.CastAsync(player.User.Id, 8002, 0, (ushort)(at.X + 1), at.Y);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & FlyStatus) != 0));
            var flew = (await this.Servers.WaitForEventAsync("game", "status.started", "name", player.User.Name))
                .GetProperty("ts").GetString();
            string since = flew;
            for (int bites = 0; bites < 2; bites++)
            {
                var bite = await this.Servers.WaitForEventAsync("game", "combat.hit", e =>
                    e.TryGetProperty("monsterName", out _) && e.GetProperty("name").GetString() == player.User.Name &&
                    string.CompareOrdinal(e.GetProperty("ts").GetString(), since) > 0);
                Assert.Equal(0u, bite.GetProperty("damage").GetUInt32());
                since = bite.GetProperty("ts").GetString();
            }
        }

        private const ulong FlyStatus = 0x8000000;

        [Fact]
        public async Task CruelShadeTakesATenthOfWhatLifeIsLeft()
        {
            var at = ServerFixture.PheasantForCruelShade;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 3, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(player, "/learn 3050");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 3050));
            await player.Game.CastAsync(player.User.Id, 3050, pheasant.Id, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 3050);
            Assert.Equal((pheasant.Id, pheasant.MonsterHealth / 10), (Assert.Single(effect.Targets).Id, effect.Targets[0].Damage));
        }

        [Fact]
        public async Task ToxicFogTakesATenthOfAPheasantsLifeEachSecondThreeTimes()
        {
            var at = ServerFixture.PheasantInFog;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 6, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(player, "/learn 6001");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 6001));

            await player.Game.CastAsync(player.User.Id, 6001, 0, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 6001));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == pheasant.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & ToxicFog) != 0));
            uint health = pheasant.MonsterHealth;
            for (int tick = 0; tick < 3; tick++)
            {
                health -= System.Math.Max(1, health / 10);
                uint expected = health;
                Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == pheasant.Id &&
                    a.Values.TryGetValue(Health, out var hp) && hp == expected));
                Assert.NotNull(await player.Game.WaitForEffectAsync(e => e.Caster == pheasant.Id && e.Type == 10010));
            }
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == pheasant.Id &&
                a.Values.TryGetValue(Status, out var s) && s == 0));
        }

        private const ulong Poisoned = 0x2, ToxicFog = 1UL << 45;

        [Fact]
        public async Task ToxicFogMissesGuards()
        {
            var at = ServerFixture.GuardToProvoke;
            await using var player = (await this.PlayingAtAsync(1002, at.X + 2, at.Y, GameMaster, level: 100)).Player;
            var guard = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(guard);
            await CommandAsync(player, "/learn 6001");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 6001));
            await FreePkAsync(player);

            await player.Game.CastAsync(player.User.Id, 6001, 0, at.X, at.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 6001);
            Assert.Equal((guard.Id, 0u), (Assert.Single(effect.Targets).Id, effect.Targets[0].Damage));
        }

        [Fact]
        public async Task RestoreHealsToFull()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 700, 840, GameMaster);
            var name = first.User.Name;
            ushort full = first.User.HealthPoints;
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync("UPDATE `character` SET HealthPoints = 10 WHERE Name = @name", ("@name", name));
            await using var player = await this.Servers.PlayAsync(email, Password);

            await CommandAsync(player, "/learn 1105");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1105));
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));
            await player.Game.CastAsync(player.User.Id, 1105, 0, 700, 840);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 1105);
            Assert.Equal(full - 10, (int)Assert.Single(effect.Targets).Damage);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Health, out var hp) && hp == full));
        }

        [Fact]
        public async Task AttrSetsAddsAndTakesAwayStats()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 820, GameMaster)).Player;
            await CommandAsync(player, "/attr spi 4");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Spirit, out var s) && s == 4
                && a.Values.TryGetValue(MaxMana, out var m) && m == 20));
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
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.ContainsKey(Health)
                && a.Values.ContainsKey(Stamina) && a.Values.TryGetValue(Mana, out var m) && m == mana));
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
            // Status bits: Iron Shirt 19, Dodge 26, Intensify 28 and Bless 30; Azure Shield 93
        [Theory]
        [InlineData(3080, 26, 60, false, 700)]
        [InlineData(5100, 19, 20, true, 710)]
        [InlineData(9000, 28, 60, false, 720)]
        [InlineData(9876, 30, 1000000, false, 730)]
        [InlineData(30000, 93, 30, false, 740)]
        public async Task StatusSpellsGiveTheCasterTheirStatus(ushort magicType, int bit, int seconds, bool xp, ushort y)
        {
            await using var player = (await this.PlayingAtAsync(1002, 740, y, GameMaster, level: 100)).Player;
            await CommandAsync(player, $"/learn {magicType}");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == magicType));
            if (xp)
            {
                await CommandAsync(player, "/xp");
                Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));
            }

            await player.Game.CastAsync(player.User.Id, magicType, player.User.Id, 0, 0);
            Assert.NotNull(await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == magicType));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id && (bit < 64
                ? a.Values.TryGetValue(Status, out var s) && (s & (1UL << bit)) != 0
                : (a.StatusHigh & (1UL << (bit - 64))) != 0)));
            var started = await this.Servers.WaitForEventAsync("game", "status.started", "name", player.User.Name);
            Assert.Equal(seconds, started.GetProperty("seconds").GetInt32());
        }

        [Fact]
        public async Task BlessEndsWhenThePlayerMoves()
        {
            await using var player = (await this.PlayingAtAsync(1002, 760, 700, GameMaster, level: 100)).Player;
            await CommandAsync(player, "/learn 9876");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 9876));
            await player.Game.CastAsync(player.User.Id, 9876, player.User.Id, 0, 0);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & (1UL << 30)) != 0));

            await player.Game.WalkAsync(player.User.Id, 0);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & (1UL << 30)) == 0));
        }
            private async Task FillXpAsync(Player player)
        {
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));
        }

        private const ulong ShurikenVortexStatus = 1UL << 46, FatalStrikeStatus = 1UL << 47;

        [Fact]
        public async Task FatalStrikeStartsOnAFullXpBarThenTurnsAMeleeClickIntoAStepOntoTheMonster()
        {
            var at = ServerFixture.PheasantForFatalStrike;
            await using var ninja = (await this.PlayingAtAsync(1002, at.X + 5, at.Y, GameMaster, level: 100)).Player;
            var pheasant = await ninja.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(ninja, "/learn 6011");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 6011));
            var katana = await GiveAsync(ninja, ServerFixture.Katana.Id);
            await ninja.Game.EquipAsync(katana.Id, 4);
            Assert.NotNull(await ninja.Game.WaitForItemActionAsync(ItemMessage.Equipped, katana.Id));
            await FillXpAsync(ninja);

            await ninja.Game.CastAsync(ninja.User.Id, 6011, ninja.User.Id, 0, 0);
            Assert.NotNull(await ninja.Game.WaitForAttribAsync(a => a.Id == ninja.User.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & FatalStrikeStatus) != 0 && (s & XpFull) == 0));

            // The client clicks the monster as for a swing, from beyond the katana's reach
            await ninja.Game.AttackAsync(ninja.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await ninja.Game.WaitForActionAsync(156, ninja.User.Id));
            var strike = await ninja.Game.WaitForInteractAsync(i => i.Sender == ninja.User.Id && i.Target == pheasant.Id);
            Assert.Equal(45u, strike.Action);

            await ninja.Game.CastAsync(ninja.User.Id, 6011, pheasant.Id, at.X, at.Y);
            Assert.Equal("xp_status", (await RejectedAsync(ninja)).GetProperty("reason").GetString());
            Assert.Equal((1002u, (int)at.X, (int)at.Y), await this.SavedPositionAsync(ninja));
        }

        [Fact]
        public async Task ShurikenVortexLastsItsStepsHitsAroundAndTakesOnlyOneAHit()
        {
            await using var ninja = (await this.PlayingAtAsync(ServerFixture.PkField, 60, 90, GameMaster, level: 100)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 61, 90, GameMaster, strength: 5000)).Player;
            await FreePkAsync(ninja);
            await FreePkAsync(victim);
            await CommandAsync(ninja, "/learn 6010");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 6010));
            await FillXpAsync(ninja);

            await ninja.Game.CastAsync(ninja.User.Id, 6010, ninja.User.Id, 60, 90);
            Assert.NotNull(await ninja.Game.WaitForAttribAsync(a => a.Id == ninja.User.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & ShurikenVortexStatus) != 0));
            var started = await this.Servers.WaitForEventAsync("game", "status.started", "name", ninja.User.Name);
            Assert.Equal(20, started.GetProperty("seconds").GetInt32());

            // The client casts it once; the vortex spins by itself every step
            var effect = await victim.Game.WaitForEffectAsync(e => e.Caster == ninja.User.Id && e.Type == 6010 &&
                e.Targets.Any(t => t.Id != ninja.User.Id));
            Assert.Equal(victim.User.Id, Assert.Single(effect.Targets).Id);

            await victim.Game.AttackAsync(victim.User.Id, ninja.User.Id, 60, 90);
            var hit = await this.Servers.WaitForEventAsync("game", "combat.hit", e =>
                e.TryGetProperty("name", out var n) && n.GetString() == ninja.User.Name &&
                e.TryGetProperty("damage", out var d) && d.GetInt32() > 0, System.TimeSpan.FromSeconds(15));
            Assert.Equal(1, hit.GetProperty("damage").GetInt32());
        }
            [Fact]
        public async Task PoisonStarKeepsAPlayerFromPotions()
        {
            await using var ninja = (await this.PlayingAtAsync(ServerFixture.PkField, 60, 80, GameMaster)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 63, 80, GameMaster)).Player;
            await FreePkAsync(ninja);
            await CommandAsync(ninja, "/learn 6002");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 6002));

            await ninja.Game.CastAsync(ninja.User.Id, 6002, victim.User.Id, 63, 80);
            Assert.NotNull(await ninja.Game.WaitForEffectAsync(e => e.Caster == ninja.User.Id && e.Type == 6002));
            Assert.NotNull(await victim.Game.WaitForAttribAsync(a => a.Id == victim.User.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & (1UL << 44)) != 0));

            var potion = await GiveAsync(victim, ServerFixture.Potion.Id);
            await victim.Game.UseAsync(potion.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", victim.User.Name);
            Assert.Equal("poison_star", refused.GetProperty("reason").GetString());
        }
            [Fact]
        public async Task GapingWoundsTakesAtLeastAFifthOfWhatLifeIsLeftTwiceAsOftenWithTwoBlades()
        {
            var at = ServerFixture.PheasantForGapingWounds;
            await using var ninja = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100, currentClass: 52)).Player;
            var pheasant = await ninja.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(ninja, "/learn 11230");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 11230));
            await ninja.Game.CastAsync(ninja.User.Id, 11230, pheasant.Id, at.X, at.Y);
            Assert.Equal("passive", (await RejectedAsync(ninja)).GetProperty("reason").GetString());

            foreach (byte hand in new byte[] { 4, 5 })
            {
                var katana = await GiveAsync(ninja, ServerFixture.Katana.Id);
                await ninja.Game.EquipAsync(katana.Id, hand);
                Assert.NotNull(await ninja.Game.WaitForItemActionAsync(ItemMessage.Equipped, katana.Id));
            }
            await ninja.Game.AttackAsync(ninja.User.Id, pheasant.Id, at.X, at.Y);
            var effect = await ninja.Game.WaitForEffectAsync(e => e.Caster == ninja.User.Id && e.Type == 11230);
            Assert.Equal(pheasant.Id, Assert.Single(effect.Targets).Id);
            Assert.True(effect.Targets[0].Damage >= pheasant.MonsterHealth / 5);
        }
            [Fact]
        public async Task GapingWoundsHitsAtLeastAsHardAsTheSwing()
        {
            var at = ServerFixture.PheasantForGapingWounds;
            await using var ninja = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, GameMaster, level: 100, strength: 5000, currentClass: 52)).Player;
            var pheasant = await ninja.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(ninja, "/learn 11230");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 11230));
            foreach (byte hand in new byte[] { 4, 5 })
            {
                var katana = await GiveAsync(ninja, ServerFixture.Katana.Id);
                await ninja.Game.EquipAsync(katana.Id, hand);
                Assert.NotNull(await ninja.Game.WaitForItemActionAsync(ItemMessage.Equipped, katana.Id));
            }

            await ninja.Game.AttackAsync(ninja.User.Id, pheasant.Id, at.X, at.Y);
            var effect = await ninja.Game.WaitForEffectAsync(e => e.Caster == ninja.User.Id && e.Type == 11230);
            Assert.True(Assert.Single(effect.Targets).Damage > pheasant.MonsterHealth / 5);
        }
            [Fact]
        public async Task CounterKillSwitchedOnTurnsAHitBackOnTheAttacker()
        {
            await using var ninja = (await this.PlayingAtAsync(ServerFixture.PkField, 66, 80, GameMaster, strength: 5000)).Player;
            await using var attacker = (await this.PlayingAtAsync(ServerFixture.PkField, 67, 80, GameMaster, strength: 50)).Player;
            await FreePkAsync(attacker);
            await CommandAsync(ninja, "/learn 6003");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 6003));

            await ninja.Game.CastAsync(ninja.User.Id, 6003, 0, 66, 80);
            Assert.NotNull(await ninja.Game.WaitForInteractAsync(i => i.Action == InteractMessage.CounterKillSwitch && i.Data == 1));

            await attacker.Game.AttackAsync(attacker.User.Id, ninja.User.Id, 66, 80);
            var back = await attacker.Game.WaitForInteractAsync(i => i.Action == InteractMessage.CounterKill);
            Assert.Equal((ninja.User.Id, attacker.User.Id), (back?.Sender, back.Target));

            await Task.Delay(600);
            await ninja.Game.CastAsync(ninja.User.Id, 6003, 0, 66, 80);
            Assert.NotNull(await ninja.Game.WaitForInteractAsync(i => i.Action == InteractMessage.CounterKillSwitch && i.Data == 0));
        }

        [Fact]
        public async Task CounterKillSwitchPacketTogglesCounterKill()
        {
            await using var ninja = (await this.PlayingAtAsync(ServerFixture.PkField, 66, 80, GameMaster, strength: 5000)).Player;
            await using var attacker = (await this.PlayingAtAsync(ServerFixture.PkField, 67, 80, GameMaster, strength: 50)).Player;
            await FreePkAsync(attacker);
            await CommandAsync(ninja, "/learn 6003");
            Assert.NotNull(await ninja.Game.WaitForMagicInfoAsync(m => m.Type == 6003));

            await ninja.Game.SwitchCounterKillAsync(ninja.User.Id);
            Assert.NotNull(await ninja.Game.WaitForInteractAsync(i => i.Action == InteractMessage.CounterKillSwitch && i.Data == 1));

            await attacker.Game.AttackAsync(attacker.User.Id, ninja.User.Id, 66, 80);
            var back = await attacker.Game.WaitForInteractAsync(i => i.Action == InteractMessage.CounterKill);
            Assert.Equal((ninja.User.Id, attacker.User.Id), (back?.Sender, back.Target));

            await Task.Delay(600);
            await ninja.Game.SwitchCounterKillAsync(ninja.User.Id);
            Assert.NotNull(await ninja.Game.WaitForInteractAsync(i => i.Action == InteractMessage.CounterKillSwitch && i.Data == 0));
        }
            private static async Task HoldBowAsync(Player player)
        {
            foreach (var (type, hand) in new[] { (ServerFixture.Bow.Id, (byte)4), (ServerFixture.Arrows.Id, (byte)5) })
            {
                var item = await GiveAsync(player, type);
                await player.Game.EquipAsync(item.Id, hand);
                Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, item.Id));
            }
        }

        private const ulong Stunned = 1UL << 58;

        [Fact]
        public async Task FreezingArrowStunsWhatTheShotHit()
        {
            var at = ServerFixture.PheasantForFreezingArrow;
            await using var archer = (await this.PlayingAtAsync(1002, at.X + 2, at.Y, GameMaster)).Player;
            var pheasant = await archer.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(archer, "/learn 5000");
            Assert.NotNull(await archer.Game.WaitForMagicInfoAsync(m => m.Type == 5000));
            await HoldBowAsync(archer);

            await archer.Game.ShootAsync(archer.User.Id, pheasant.Id, at.X, at.Y);
            var effect = await archer.Game.WaitForEffectAsync(e => e.Caster == archer.User.Id && e.Type == 5000);
            Assert.Equal(pheasant.Id, Assert.Single(effect.Targets).Id);
            Assert.NotNull(await archer.Game.WaitForAttribAsync(a => a.Id == pheasant.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & Stunned) != 0));
        }

        [Fact]
        public async Task PoisonousArrowsPoisonWhatTheShotHit()
        {
            var at = ServerFixture.PheasantForPoisonousArrows;
            await using var archer = (await this.PlayingAtAsync(1002, at.X + 2, at.Y, GameMaster)).Player;
            var pheasant = await archer.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await CommandAsync(archer, "/learn 5002");
            Assert.NotNull(await archer.Game.WaitForMagicInfoAsync(m => m.Type == 5002));
            await HoldBowAsync(archer);

            await archer.Game.ShootAsync(archer.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await archer.Game.WaitForEffectAsync(e => e.Caster == archer.User.Id && e.Type == 5002));
            Assert.NotNull(await archer.Game.WaitForAttribAsync(a => a.Id == pheasant.Id &&
                a.Values.TryGetValue(Status, out var s) && (s & Poisoned) != 0));
        }
            [Fact]
        public async Task DashHitsWhoIsInTheWayAndKnocksThemACellBack()
        {
            await using var warrior = (await this.PlayingAtAsync(ServerFixture.PkField, 64, 70, GameMaster, strength: 50)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.PkField, 65, 70)).Player;
            await FreePkAsync(warrior);
            await CommandAsync(warrior, "/learn 1051");
            Assert.NotNull(await warrior.Game.WaitForMagicInfoAsync(m => m.Type == 1051));
            await warrior.Game.CastAsync(warrior.User.Id, 1051, 0, 65, 70);
            Assert.Equal("wrong_weapon", (await RejectedAsync(warrior)).GetProperty("reason").GetString());
            foreach (var (type, hand) in new[] { (ServerFixture.Blade.Id, (byte)4), (ServerFixture.Shield.Id, (byte)5) })
            {
                var item = await GiveAsync(warrior, type);
                await warrior.Game.EquipAsync(item.Id, hand);
                Assert.NotNull(await warrior.Game.WaitForItemActionAsync(ItemMessage.Equipped, item.Id));
            }

            await warrior.Game.CastAsync(warrior.User.Id, 1051, 0, 65, 70);
            var bump = await victim.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Dash && i.Sender == warrior.User.Id);
            Assert.Equal((victim.User.Id, (ushort)65, (ushort)70), (bump?.Target, bump.X, bump.Y));
            Assert.Equal(6u, bump.Data >> 24);
            await SyncAsync(victim);
            Assert.Equal((ServerFixture.PkField, 66, 70), await this.SavedPositionAsync(victim));
            Assert.Equal((ServerFixture.PkField, 65, 70), await this.SavedPositionAsync(warrior));
        }
            [Fact]
        public async Task GolemTurnsThePlayerIntoItsMonsterForItsStep()
        {
            await using var player = (await this.PlayingAtAsync(1002, 760, 720, GameMaster, level: 100)).Player;
            await CommandAsync(player, "/learn 1270");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1270));
            await FillXpAsync(player);

            await player.Game.CastAsync(player.User.Id, 1270, player.User.Id, 760, 720);
            var look = await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Mesh, out var m) && m >= 10_000_000);
            Assert.Equal((214u, player.User.Mesh % 10_000_000), ((uint)(look.Values[Mesh] / 10_000_000), (uint)(look.Values[Mesh] % 10_000_000)));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(MaxHealth, out var h) && h == 50000));

            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Mesh, out var m) && m < 10_000_000,
                System.TimeSpan.FromSeconds(10)));
        }
    }
}

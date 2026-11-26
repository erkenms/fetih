namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    public class TrainingGroundTests : IntegrationTest
    {
        public TrainingGroundTests(ServerFixture servers) : base(servers) { }

        private const uint Boxer = 101607, BoxerHuang = 600075;
        private static readonly TimeSpan Already = TimeSpan.FromMilliseconds(300);
        private static readonly (uint Id, ushort X, ushort Y) Lv20Stake = (100011, 20, 220), Lv25Stake = (100087, 132, 108),
            Lv20Scarecrow = (100049, 10, 210);

        private Task<System.Text.Json.JsonElement> RejectedAsync(Player player, string kind, int after = 0) =>
            this.Servers.WaitForEventAsync("game", kind + ".rejected", "name", player.User.Name, after);

        [Fact]
        public async Task TheBoxerTakesPlayersOfLevel20ForAFareAndBoxerHuangSendsThemBack()
        {
            await using var player = (await this.PlayingAtAsync(1002, 352, 336, GameMaster)).Player;
            await CommandAsync(player, "/silver +1500");
            var given = await player.Game.WaitForAttribAsync(a => a.Values.ContainsKey(Silver));
            ulong silver = given.Values[Silver];

            await player.Game.TalkToNpcAsync(Boxer);
            Assert.Contains("level 20", (await player.Game.ReadDialogAsync())?[0].Text);
            await player.Game.AnswerNpcAsync(1);
            Assert.Contains("level 20 or above", (await player.Game.ReadDialogAsync())?[0].Text);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id, Already));

            await CommandAsync(player, "/level 20");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Level, out var l) && l == 20));
            await player.Game.TalkToNpcAsync(Boxer);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            var moved = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((ServerFixture.TrainingGround, (ushort)217, (ushort)215), (moved?.Command, moved?.X, moved?.Y));
            Assert.NotNull(await SilverAsync(player, silver - 1000));
            Assert.NotNull(await player.Game.WaitForNpcAsync(BoxerHuang));

            await player.Game.TalkToNpcAsync(BoxerHuang);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            var back = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((1002u, (ushort)352, (ushort)336), (back?.Command, back?.X, back?.Y));
        }

        [Fact]
        public async Task StakesOfTheirLevelGiveExperienceAndCostNoArrows()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.TrainingGround,
                Lv20Stake.X + 2, Lv20Stake.Y, GameMaster, level: 20)).Player;
            var stake = await player.Game.WaitForGoalAsync(Lv20Stake.Id);
            Assert.Equal((10000u, 10000u, (ushort)427, (ushort)21), (stake?.MaxLife, stake.Life, stake.Lookface, stake.Role));

            var bow = await GiveAsync(player, ServerFixture.Bow.Id);
            var arrows = await GiveAsync(player, ServerFixture.Arrows.Id);
            await player.Game.EquipAsync(bow.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bow.Id));
            await player.Game.EquipAsync(arrows.Id, 5);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, arrows.Id));

            await player.Game.ShootAsync(player.User.Id, Lv20Stake.Id, Lv20Stake.X, Lv20Stake.Y);
            var shot = await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Shoot && i.Target == Lv20Stake.Id);
            Assert.True(shot?.Data > 0);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Shoot && i.Target == Lv20Stake.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Experience, out var e) && e > 0));
            Assert.Null(await player.Game.WaitForItemAsync(i => i.Id == arrows.Id && i.Amount < ServerFixture.Arrows.Amount, Already));

            await player.Game.ShootAsync(player.User.Id, Lv25Stake.Id, Lv25Stake.X, Lv25Stake.Y);
            Assert.Equal("goal_level", (await RejectedAsync(player, "combat")).GetProperty("reason").GetString());
            int mark = this.Servers.Mark();
            await player.Game.ShootAsync(player.User.Id, Lv20Scarecrow.Id, Lv20Scarecrow.X, Lv20Scarecrow.Y);
            Assert.Equal("scarecrow_needs_spell", (await RejectedAsync(player, "combat", mark)).GetProperty("reason").GetString());
        }

        [Fact]
        public async Task SpellsOnScarecrowsCostNoManaAndLevelTheSpell()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.TrainingGround,
                Lv20Scarecrow.X + 3, Lv20Scarecrow.Y, GameMaster, level: 20)).Player;
            Assert.NotNull(await player.Game.WaitForGoalAsync(Lv20Scarecrow.Id));
            await CommandAsync(player, "/learn 1000");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1000 && m.Level == 0));

            await player.Game.CastAsync(player.User.Id, 1000, Lv20Scarecrow.Id, Lv20Scarecrow.X, Lv20Scarecrow.Y);
            var effect = await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 1000);
            Assert.Equal(Lv20Scarecrow.Id, Assert.Single(effect.Targets).Id);
            Assert.True(effect.Targets[0].Damage >= 1000);
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1000 && m.Level == 1));

            await CommandAsync(player, "/learn 1001");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1001));
            await player.Game.CastAsync(player.User.Id, 1001, Lv20Scarecrow.Id, Lv20Scarecrow.X, Lv20Scarecrow.Y);
            Assert.NotNull(await player.Game.WaitForEffectAsync(e => e.Caster == player.User.Id && e.Type == 1001));

            await player.Game.CastAsync(player.User.Id, 1000, Lv20Stake.Id, Lv20Stake.X, Lv20Stake.Y);
            Assert.Equal("stake_needs_weapon", (await RejectedAsync(player, "magic")).GetProperty("reason").GetString());
        }

        [Fact]
        public async Task AStakeThatRunsOutOfLifeStandsAgainFull()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.TrainingGround, 25, 216, GameMaster,
                level: 20, strength: 5000)).Player;
            Assert.NotNull(await player.Game.WaitForGoalAsync(100013));
            await player.Game.AttackAsync(player.User.Id, 100013, 24, 216);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == 100013 &&
                a.Values.TryGetValue(Health, out var life) && life == 10000, TimeSpan.FromSeconds(20)));
        }

        [Fact]
        public async Task WindstormGoesOffOnAStake()
        {
            await using var pirate = (await this.PlayingAtAsync(ServerFixture.TrainingGround, 29, 212, GameMaster, level: 100)).Player;
            Assert.NotNull(await pirate.Game.WaitForGoalAsync(100015));
            var rapier = await GiveAsync(pirate, ServerFixture.Rapier.Id);
            await pirate.Game.EquipAsync(rapier.Id, 4);
            Assert.NotNull(await pirate.Game.WaitForItemActionAsync(ItemMessage.Equipped, rapier.Id));
            await CommandAsync(pirate, "/learn 11140");
            Assert.NotNull(await pirate.Game.WaitForMagicInfoAsync(m => m.Type == 11140));

            await pirate.Game.AttackAsync(pirate.User.Id, 100015, 28, 212);
            var combo = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11140);
            Assert.Equal(3, combo.Targets.Count);
            Assert.All(combo.Targets, t => Assert.Equal(100015u, t.Id));
        }

        [Fact]
        public async Task BladeTempestHitsTheScarecrowsOnItsWay()
        {
            await using var pirate = (await this.PlayingAtAsync(ServerFixture.TrainingGround, 22, 198, GameMaster, level: 100)).Player;
            Assert.NotNull(await pirate.Game.WaitForGoalAsync(100053));
            var rapier = await GiveAsync(pirate, ServerFixture.Rapier.Id);
            await pirate.Game.EquipAsync(rapier.Id, 4);
            Assert.NotNull(await pirate.Game.WaitForItemActionAsync(ItemMessage.Equipped, rapier.Id));
            await CommandAsync(pirate, "/learn 11110");
            Assert.NotNull(await pirate.Game.WaitForMagicInfoAsync(m => m.Type == 11110));

            await CommandAsync(pirate, "/restore");
            var rested = await pirate.Game.WaitForAttribAsync(a => a.Values.ContainsKey(Stamina));
            ulong full = rested.Values[Stamina];

            await pirate.Game.CastAsync(pirate.User.Id, 11110, 0, 14, 206);
            var effect = await pirate.Game.WaitForEffectAsync(e => e.Caster == pirate.User.Id && e.Type == 11110);
            Assert.Contains(effect.Targets, t => t.Id == 100053 && t.Damage > 0);
            Assert.DoesNotContain(effect.Targets, t => t.Id == 100013 || t.Id == 100015);
            // It costs 20 stamina elsewhere
            await SyncAsync(pirate);
            Assert.Null(await pirate.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Stamina, out var s) && s < full, Already));
        }
    }
}

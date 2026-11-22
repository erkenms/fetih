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
        private const ulong Fatigue = 1UL << (119 - 64);

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
                a.Values.ContainsKey(Status) && (a.HighStatus & Fatigue) != 0));

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
    }
}

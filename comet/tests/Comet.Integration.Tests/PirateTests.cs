namespace Comet.Integration.Tests
{
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
    }
}

namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Skills from class masters and books, and the spells of ServerFixture they lead to
    public class SkillLearningTests : IntegrationTest
    {
        private const uint TrojanStar = 10022;
        private const byte Teach = 4, Cyclone = 10, Hercules = 12;

        public SkillLearningTests(ServerFixture servers) : base(servers) { }

        private static async Task<System.Collections.Generic.List<DialogMessage>> TalkAsync(Player player, uint npc, byte option)
        {
            await player.Game.TalkToNpcAsync(npc);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(option);
            return await player.Game.ReadDialogAsync();
        }

        private static async Task<string> AnswerAsync(Player player, byte option)
        {
            await player.Game.AnswerNpcAsync(option);
            return (await player.Game.ReadDialogAsync())?[0].Text;
        }

        private async Task<Player> AtJobCenterAsync(int x, byte level, byte currentClass)
        {
            var player = (await this.PlayingAtAsync(ServerFixture.JobCenter, x, 38, GameMaster, level)).Player;
            await CommandAsync(player, $"/class {currentClass}");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                a.Values.TryGetValue(Class, out var c) && c == currentClass));
            return player;
        }

        [Fact]
        public async Task TheTrojanStarTeachesTrojanSkillsAsTheyReachTheirLevels()
        {
            await using var player = await this.AtJobCenterAsync(46, 3, 10);

            var page = await TalkAsync(player, TrojanStar, Teach);
            Assert.Equal(new[] { "Cyclone" }, page.Where(p => p.Action == DialogMessage.Choice && p.Option != DialogMessage.NoOption)
                .Select(p => p.Text));
            Assert.Contains("learned Cyclone", await AnswerAsync(player, Cyclone));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1110 && m.Level == 0));

            Assert.Contains("At level 40 I can teach you Hercules", (await TalkAsync(player, TrojanStar, Teach))[0].Text);
            Assert.Contains("level 40", await AnswerAsync(player, Hercules));
            var refused = await this.Servers.WaitForEventAsync("game", "magic.learn_rejected", "name", player.User.Name);
            Assert.Equal("level", refused.GetProperty("reason").GetString());

            await CommandAsync(player, "/level 40");
            Assert.NotNull(await TalkAsync(player, TrojanStar, Teach));
            Assert.Contains("learned Hercules", await AnswerAsync(player, Hercules));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1115));
        }

        [Fact]
        public async Task MastersTeachOnlyTheirOwnLine()
        {
            await using var player = await this.AtJobCenterAsync(50, 40, 100);
            Assert.Contains("only teach trojans", (await TalkAsync(player, TrojanStar, Teach))[0].Text);
        }

        [Fact]
        public async Task AThunderBookTeachesTaoistsAndStaysWithOthers()
        {
            await using var player = (await this.PlayingAtAsync(1002, 760, 960, GameMaster)).Player;
            await CommandAsync(player, "/class 10");
            var book = await GiveAsync(player, ServerFixture.ThunderBook.Id);

            await player.Game.UseAsync(book.Id);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "Only taoists can learn Thunder."));
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name);
            Assert.Equal("class", refused.GetProperty("reason").GetString());

            await CommandAsync(player, "/class 100");
            await player.Game.UseAsync(book.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, book.Id));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1000 && m.Level == 0));
            Assert.Equal(0L, (long)await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id)));

            var second = await GiveAsync(player, ServerFixture.ThunderBook.Id);
            await player.Game.UseAsync(second.Id);
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "You have already learned Thunder."));
        }

        [Fact]
        public async Task MeditationRefusesAFullManaBar()
        {
            await using var player = (await this.PlayingAtAsync(1002, 780, 960, GameMaster, level: 50)).Player;
            await CommandAsync(player, "/learn 1195");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1195));
            await CommandAsync(player, "/restore");
            await player.Game.CastAsync(player.User.Id, 1195, 0, 780, 960);
            Assert.Equal("full_mana", (await this.Servers.WaitForEventAsync("game", "magic.rejected", "name", player.User.Name))
                .GetProperty("reason").GetString());
        }
    }
}

namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    // Skills from class masters and books, and the spells of ServerFixture they lead to
    public class SkillLearningTests : IntegrationTest
    {
        public SkillLearningTests(ServerFixture servers) : base(servers) { }

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
    }
}

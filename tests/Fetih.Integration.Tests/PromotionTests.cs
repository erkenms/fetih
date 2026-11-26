namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    // The class masters of the Job Center, at their places in Data/npcs.json
    public class PromotionTests : IntegrationTest
    {
        private const uint TrojanStar = 10022, TaoistMoon = 10000, PirateLord = 10070;
        private const byte Promote = 1, Fire = 3;

        public PromotionTests(ServerFixture servers) : base(servers) { }

        private static Task<UserAttribMessage> ClassAsync(Player player, byte currentClass) =>
            player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                a.Values.TryGetValue(Class, out var c) && c == currentClass);

        private static async Task<string> TalkAsync(Player player, uint npc)
        {
            await player.Game.TalkToNpcAsync(npc);
            return (await player.Game.ReadDialogAsync())?[0].Text;
        }

        private static async Task<string> AnswerAsync(Player player, byte option)
        {
            await player.Game.AnswerNpcAsync(option);
            return (await player.Game.ReadDialogAsync())?[0].Text;
        }

        private async Task<Player> AtJobCenterAsync(int x, byte level, byte currentClass)
        {
            var player = (await this.PlayingAtAsync(ServerFixture.JobCenter, x, 36, GameMaster, level)).Player;
            await CommandAsync(player, $"/class {currentClass}");
            Assert.NotNull(await ClassAsync(player, currentClass));
            return player;
        }

        [Fact]
        public async Task ATrojanIsPromotedAtLevelFifteenAndForAnEmeraldAtSeventy()
        {
            await using var player = await this.AtJobCenterAsync(44, 14, 10);

            Assert.Contains("level 15", await TalkAsync(player, TrojanStar));
            Assert.Contains("level 15", await AnswerAsync(player, Promote));
            var refused = await this.Servers.WaitForEventAsync("game", "character.promotion_rejected", "name", player.User.Name);
            Assert.Equal("level", refused.GetProperty("reason").GetString());

            await CommandAsync(player, "/level 15");
            Assert.NotNull(await TalkAsync(player, TrojanStar));
            Assert.Contains("now a Trojan", await AnswerAsync(player, Promote));
            Assert.NotNull(await ClassAsync(player, 11));

            await CommandAsync(player, "/class 12");
            await CommandAsync(player, "/level 70");
            Assert.Contains("Emerald", await TalkAsync(player, TrojanStar));
            Assert.Contains("Emerald", await AnswerAsync(player, Promote));
            await GiveAsync(player, ServerFixture.Emerald.Id);
            Assert.NotNull(await TalkAsync(player, TrojanStar));
            Assert.Contains("now a Tiger Trojan", await AnswerAsync(player, Promote));
            Assert.NotNull(await ClassAsync(player, 13));

            var promoted = await this.Servers.WaitForEventAsync("game", "character.promoted", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("class").GetInt32() == 13);
            Assert.Equal(ServerFixture.Emerald.Id, promoted.GetProperty("itemTypeId").GetUInt32());
            Assert.Equal(13L, System.Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT CurrentClass FROM `character` WHERE CharacterID = @id", ("@id", player.User.Id))));
            Assert.Equal(0L, (long)await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id)));
        }

        [Fact]
        public async Task ATaoistChoosesFireAtLevelFortyAndOnlyAtTheTaoistMaster()
        {
            await using var player = await this.AtJobCenterAsync(56, 40, 101);

            Assert.Contains("only promote trojans", await TalkAsync(player, TrojanStar));
            Assert.Contains("only promote trojans", await AnswerAsync(player, Promote));
            var refused = await this.Servers.WaitForEventAsync("game", "character.promotion_rejected", "name", player.User.Name);
            Assert.Equal("other_line", refused.GetProperty("reason").GetString());

            Assert.Contains("water or fire", await TalkAsync(player, TaoistMoon));
            Assert.Contains("water or fire", await AnswerAsync(player, Promote));
            Assert.Contains("now a Fire Taoist", await AnswerAsync(player, Fire));
            Assert.NotNull(await ClassAsync(player, 142));
        }

        [Fact]
        public async Task ThePirateLordPromotesPirates()
        {
            await using var player = await this.AtJobCenterAsync(60, 15, 70);

            Assert.Contains("Pirate", await TalkAsync(player, PirateLord));
            Assert.Contains("now a Pirate", await AnswerAsync(player, Promote));
            Assert.NotNull(await ClassAsync(player, 71));
        }
    }
}

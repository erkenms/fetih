namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class MessageBoardTests : IntegrationTest
    {
        public MessageBoardTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task PlayersPostListViewReplaceAndDeleteOnMessageBoard()
        {
            await using var alice = (await this.PlayingAtAsync(1002, 440, 380)).Player;
            await using var bob = (await this.PlayingAtAsync(1002, 442, 380)).Player;

            var fullText = "Selling DragonBall at Market 180,220 - whisper me anytime for a fair price!";
            await alice.Game.SayAsync(TalkChannels.TradeBoard, alice.User.Name, "ALL", fullText);
            await this.Servers.WaitForEventAsync("game", "message_board.posted", "name", alice.User.Name);

            // Bob lists the Trade channel and sees Alice's entry as a (name, preview, yyyyMMddHHmm) triple
            await bob.Game.MessageBoardAsync(0, TalkChannels.TradeBoard, MessageBoardMessage.GetList);
            var list = await bob.Game.WaitForMessageBoardAsync(
                m => m.Channel == TalkChannels.TradeBoard && m.Action == MessageBoardMessage.List);
            Assert.NotNull(list);
            int idx = list.Strings.IndexOf(alice.User.Name);
            Assert.True(idx >= 0 && idx % 3 == 0);
            Assert.Equal(fullText[..44], list.Strings[idx + 1]);
            Assert.Equal(12, list.Strings[idx + 2].Length);

            // Viewing returns the full message on the same board channel
            await bob.Game.MessageBoardAsync(0, TalkChannels.TradeBoard, MessageBoardMessage.GetWords, alice.User.Name);
            var words = await bob.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.TradeBoard && t.Sender == alice.User.Name);
            Assert.NotNull(words);
            Assert.Equal(fullText, words.Text);

            // Bob cannot delete Alice's post
            await bob.Game.MessageBoardAsync(0, TalkChannels.TradeBoard, MessageBoardMessage.Delete, alice.User.Name);
            await this.Servers.WaitForEventAsync("game", "message_board.rejected", "name", bob.User.Name);

            // Submitting another post on the same channel replaces Alice's first post
            await alice.Game.SayAsync(TalkChannels.TradeBoard, alice.User.Name, "ALL", "Sold out!");
            await this.Servers.WaitForEventAsync("game", "message_board.posted",
                e => e.GetProperty("name").GetString() == alice.User.Name &&
                     e.GetProperty("text").GetString() == "Sold out!");

            await bob.Game.MessageBoardAsync(0, TalkChannels.TradeBoard, MessageBoardMessage.GetWords, alice.User.Name);
            var replaced = await bob.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.TradeBoard && t.Sender == alice.User.Name && t.Text == "Sold out!");
            Assert.NotNull(replaced);

            // Alice deletes her own post
            await alice.Game.MessageBoardAsync(0, TalkChannels.TradeBoard, MessageBoardMessage.Delete, alice.User.Name);
            await this.Servers.WaitForEventAsync("game", "message_board.deleted", "name", alice.User.Name);

            await bob.Game.MessageBoardAsync(0, TalkChannels.TradeBoard, MessageBoardMessage.GetList);
            var afterDelete = await bob.Game.WaitForMessageBoardAsync(
                m => m.Channel == TalkChannels.TradeBoard && m.Action == MessageBoardMessage.List);
            Assert.DoesNotContain(alice.User.Name, afterDelete.Strings);
        }

        [Fact]
        public async Task GuildBoardRequiresGuildAndIsolatesOtherGuilds()
        {
            await using var loner = (await this.PlayingAtAsync(1002, 445, 380)).Player;
            await loner.Game.SayAsync(TalkChannels.GuildBoard, loner.User.Name, "ALL", "no guild");
            var notice = await loner.Game.WaitForTalkAsync(
                t => t.Channel == TalkChannels.Service && t.Text.Contains("not in a guild"));
            Assert.NotNull(notice);

            await loner.Game.MessageBoardAsync(0, TalkChannels.GuildBoard, MessageBoardMessage.GetList);
            var empty = await loner.Game.WaitForMessageBoardAsync(
                m => m.Channel == TalkChannels.GuildBoard && m.Action == MessageBoardMessage.List);
            Assert.Empty(empty.Strings);
        }
    }
}

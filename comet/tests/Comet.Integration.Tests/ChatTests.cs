namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class ChatTests : IntegrationTest
    {
        private const ushort Moderator = 2, GameMaster = 3;
        private static readonly TimeSpan Silence = TimeSpan.FromSeconds(1);

        public ChatTests(ServerFixture servers) : base(servers) { }

        private static Task<TalkMessage> NoticeAsync(Player player, string containing) =>
            player.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Service && t.Text.Contains(containing));

        [Fact]
        public async Task TalkReachesOnlyPlayersInViewUnderTheSendersName()
        {
            await using var speaker = (await this.PlayingAtAsync(1002, 520, 520)).Player;
            await using var near = (await this.PlayingAtAsync(1002, 525, 520)).Player;
            await using var far = (await this.PlayingAtAsync(1002, 600, 600)).Player;

            await speaker.Game.SayAsync(TalkChannels.Talk, "Impostor", "ALLUSERS", "hello there");
            var heard = await near.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk);
            Assert.NotNull(heard);
            Assert.Equal((speaker.User.Name, "hello there"), (heard.Sender, heard.Text));
            Assert.Null(await far.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk, Silence));
            Assert.Null(await speaker.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk, Silence));

            var logged = await this.Servers.WaitForEventAsync("game", "chat.message", "name", speaker.User.Name);
            Assert.Equal("Talk", logged.GetProperty("channel").GetString());
            Assert.Equal("hello there", logged.GetProperty("text").GetString());
        }

        [Fact]
        public async Task LongAndNonAsciiTextIsRelayedByteForByte()
        {
            await using var speaker = (await this.PlayingAtAsync(1002, 540, 520)).Player;
            await using var near = (await this.PlayingAtAsync(1002, 541, 520)).Player;
            var text = "Çağrı şöyle dedi: " + new string('x', 200);

            await speaker.Game.SayAsync(TalkChannels.Talk, speaker.User.Name, "ALLUSERS", text);
            var heard = await near.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk);
            Assert.NotNull(heard);
            Assert.Equal(System.Text.Encoding.Latin1.GetBytes(text),
                System.Text.Encoding.Latin1.GetBytes(heard.Text));
        }

        [Fact]
        public async Task WhisperReachesTheRecipientOnAnyMap()
        {
            await using var sender = (await this.PlayingAtAsync(1002, 560, 520)).Player;
            await using var bystander = (await this.PlayingAtAsync(1002, 561, 520)).Player;
            await using var recipient = (await this.PlayingAtAsync(1006, 20, 20)).Player;

            await sender.Game.SayAsync(TalkChannels.Whisper, sender.User.Name,
                recipient.User.Name.ToLowerInvariant(), "psst");
            var heard = await recipient.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Whisper);
            Assert.NotNull(heard);
            Assert.Equal((sender.User.Name, recipient.User.Name, "psst"), (heard.Sender, heard.Recipient, heard.Text));
            Assert.Null(await bystander.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Whisper, Silence));
        }

        [Fact]
        public async Task WhisperingSomeoneOfflineTellsTheSender()
        {
            await using var sender = (await this.PlayingAtAsync(1002, 580, 520)).Player;
            await sender.Game.SayAsync(TalkChannels.Whisper, sender.User.Name, "Nobody", "anyone?");
            Assert.NotNull(await NoticeAsync(sender, "Nobody is not online."));
            var rejected = await this.Servers.WaitForEventAsync("game", "chat.rejected", "name", sender.User.Name);
            Assert.Equal("offline", rejected.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task WorldReachesEveryoneOnceAPeriod()
        {
            await using var speaker = (await this.PlayingAtAsync(1002, 600, 520, level: 70)).Player;
            await using var elsewhere = (await this.PlayingAtAsync(1006, 25, 25)).Player;

            await speaker.Game.SayAsync(TalkChannels.World, speaker.User.Name, "ALLUSERS", "first");
            var heard = await elsewhere.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.World);
            Assert.NotNull(heard);
            Assert.Equal((speaker.User.Name, "first"), (heard.Sender, heard.Text));

            await speaker.Game.SayAsync(TalkChannels.World, speaker.User.Name, "ALLUSERS", "second");
            Assert.NotNull(await NoticeAsync(speaker, "world channel again"));
            Assert.Null(await elsewhere.Game.WaitForTalkAsync(t => t.Text == "second", Silence));
        }

        [Fact]
        public async Task WorldNeedsLevel70()
        {
            await using var speaker = (await this.PlayingAtAsync(1002, 610, 520, level: 69)).Player;
            await using var elsewhere = (await this.PlayingAtAsync(1006, 26, 26)).Player;
            await speaker.Game.SayAsync(TalkChannels.World, speaker.User.Name, "ALLUSERS", "too young");
            var rejected = await this.Servers.WaitForEventAsync("game", "chat.rejected", "name", speaker.User.Name);
            Assert.Equal("level", rejected.GetProperty("reason").GetString());
            Assert.Null(await elsewhere.Game.WaitForTalkAsync(t => t.Text == "too young", Silence));
            Assert.Null(await speaker.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Service, Silence));
        }

        [Fact]
        public async Task WhisperDetailsDescribeTheOtherPlayer()
        {
            await using var asker = (await this.PlayingAtAsync(1002, 630, 520)).Player;
            await using var other = (await this.PlayingAtAsync(1006, 15, 15, level: 42)).Player;

            var details = await asker.Game.AskWhisperDetailsAsync(other.User.Name.ToUpperInvariant());
            Assert.NotNull(details);
            Assert.Equal(other.User.Name, details.Strings[0]);
            Assert.StartsWith($"{other.User.Id} 42 ", details.Strings[1]);
            Assert.Null(await asker.Game.AskWhisperDetailsAsync("Nobody", Silence));
        }

        [Fact]
        public async Task CommandsFromPlayersAreOrdinaryChat()
        {
            await using var player = (await this.PlayingAtAsync(1002, 620, 520)).Player;
            await using var near = (await this.PlayingAtAsync(1002, 621, 520)).Player;

            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/tp 1006 30 30");
            var heard = await near.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk);
            Assert.Equal("/tp 1006 30 30", heard?.Text);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id, Silence));
        }

        [Fact]
        public async Task GameMasterTeleportsAndListsWhoIsOnline()
        {
            await using var gm = (await this.PlayingAtAsync(1002, 640, 520, GameMaster)).Player;
            await using var other = (await this.PlayingAtAsync(1006, 10, 10)).Player;

            await gm.Game.SayAsync(TalkChannels.Talk, gm.User.Name, "ALLUSERS", "/online");
            Assert.NotNull(await NoticeAsync(gm, other.User.Name));

            await gm.Game.SayAsync(TalkChannels.Talk, gm.User.Name, "ALLUSERS", "/teleport 1006 30 30");
            var moved = await gm.Game.WaitForActionAsync(ActionMessage.Teleport, gm.User.Id);
            Assert.NotNull(moved);
            Assert.Equal((1006u, 30, 30), (moved.Command, (int)moved.X, (int)moved.Y));

            await gm.Game.SayAsync(TalkChannels.Talk, gm.User.Name, "ALLUSERS", "/goto " + other.User.Name.ToLowerInvariant());
            moved = await gm.Game.WaitForActionAsync(ActionMessage.Teleport, gm.User.Id);
            Assert.Equal((1006u, 10, 10), (moved.Command, (int)moved.X, (int)moved.Y));

            await gm.Game.SayAsync(TalkChannels.Talk, gm.User.Name, "ALLUSERS", "/tp 1006 x");
            Assert.NotNull(await NoticeAsync(gm, "Usage: /tp"));

            var logged = await this.Servers.WaitForEventAsync("game", "gm.command", "name", gm.User.Name);
            Assert.Equal("online", logged.GetProperty("command").GetString());
            Assert.Equal("ok", logged.GetProperty("result").GetString());
        }

        [Fact]
        public async Task ModeratorCanKickButNotTeleport()
        {
            await using var mod = (await this.PlayingAtAsync(1002, 660, 520, Moderator)).Player;
            await using var target = (await this.PlayingAtAsync(1002, 661, 520)).Player;

            await mod.Game.SayAsync(TalkChannels.Talk, mod.User.Name, "ALLUSERS", "/tp 1006 30 30");
            Assert.NotNull(await NoticeAsync(mod, "Unknown command /tp"));

            await mod.Game.SayAsync(TalkChannels.Talk, mod.User.Name, "ALLUSERS", "/kick " + target.User.Name);
            Assert.True(await target.Game.WaitClosedAsync());
            await this.Servers.WaitForEventAsync("game", "session.logout", e =>
                e.GetProperty("name").GetString() == target.User.Name &&
                e.GetProperty("reason").GetString() == "kicked");
        }
    }
}

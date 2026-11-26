namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class ChatTests : IntegrationTest
    {
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
            await SyncAsync(speaker, far);
            await SyncAsync(speaker);
            Assert.Null(await far.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk, Already));
            Assert.Null(await speaker.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk, Already));

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
            await SyncAsync(sender, bystander);
            Assert.Null(await bystander.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Whisper, Already));
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
            await SyncAsync(speaker, elsewhere);
            Assert.Null(await elsewhere.Game.WaitForTalkAsync(t => t.Text == "second", Already));
        }

        [Fact]
        public async Task WorldNeedsLevel70()
        {
            await using var speaker = (await this.PlayingAtAsync(1002, 610, 520, level: 69)).Player;
            await using var elsewhere = (await this.PlayingAtAsync(1006, 26, 26)).Player;
            await speaker.Game.SayAsync(TalkChannels.World, speaker.User.Name, "ALLUSERS", "too young");
            var rejected = await this.Servers.WaitForEventAsync("game", "chat.rejected", "name", speaker.User.Name);
            Assert.Equal("level", rejected.GetProperty("reason").GetString());
            await SyncAsync(speaker, elsewhere);
            await SyncAsync(speaker);
            Assert.Null(await elsewhere.Game.WaitForTalkAsync(t => t.Text == "too young", Already));
            Assert.Null(await speaker.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Service, Already));
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
            await asker.Game.SendAsync(NameMessage.Build(NameMessage.WhisperDetails, "Nobody"));
            await SyncAsync(asker);
            Assert.Null(await asker.Game.WaitForNameAsync(n => n.Action == NameMessage.WhisperDetails, Already));
        }

        [Fact]
        public async Task CommandsFromPlayersAreOrdinaryChat()
        {
            await using var player = (await this.PlayingAtAsync(1002, 620, 520)).Player;
            await using var near = (await this.PlayingAtAsync(1002, 621, 520)).Player;

            await CommandAsync(player, "/tp 1006 30 30");
            var heard = await near.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Talk);
            Assert.Equal("/tp 1006 30 30", heard?.Text);
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id, Already));
        }

        [Fact]
        public async Task GameMasterTeleportsAndListsWhoIsOnline()
        {
            await using var gm = (await this.PlayingAtAsync(1002, 640, 520, GameMaster)).Player;
            await using var other = (await this.PlayingAtAsync(1006, 10, 10)).Player;

            await CommandAsync(gm, "/online");
            Assert.NotNull(await NoticeAsync(gm, other.User.Name));

            await CommandAsync(gm, "/teleport 1006 30 30");
            var moved = await gm.Game.WaitForActionAsync(ActionMessage.Teleport, gm.User.Id);
            Assert.NotNull(moved);
            Assert.Equal((1006u, 30, 30), (moved.Command, (int)moved.X, (int)moved.Y));

            await CommandAsync(gm, "/grab " + other.User.Name);
            moved = await other.Game.WaitForActionAsync(ActionMessage.Teleport, other.User.Id);
            Assert.Equal((1006u, 30, 30), (moved.Command, (int)moved.X, (int)moved.Y));

            await CommandAsync(gm, "/goto " + other.User.Name.ToLowerInvariant());
            moved = await gm.Game.WaitForActionAsync(ActionMessage.Teleport, gm.User.Id);
            Assert.Equal((1006u, 30, 30), (moved.Command, (int)moved.X, (int)moved.Y));

            await CommandAsync(gm, "/tp 1006 x");
            Assert.NotNull(await NoticeAsync(gm, "Usage: /tp"));

            var logged = await this.Servers.WaitForEventAsync("game", "gm.command", "name", gm.User.Name);
            Assert.Equal("online", logged.GetProperty("command").GetString());
            Assert.Equal("ok", logged.GetProperty("result").GetString());
        }

        [Fact]
        public async Task GameMasterTeleportsByPlaceName()
        {
            await using var gm = (await this.PlayingAtAsync(1002, 680, 520, GameMaster)).Player;

            async Task<(uint, int, int)> TeleportAsync(string command)
            {
                await CommandAsync(gm, command);
                var moved = await gm.Game.WaitForActionAsync(ActionMessage.Teleport, gm.User.Id);
                Assert.NotNull(moved);
                return (moved.Command, moved.X, moved.Y);
            }

            Assert.Equal((ServerFixture.Market, 303, 237), await TeleportAsync("/tp market"));
            Assert.Equal((ServerFixture.Market, 40, 50), await TeleportAsync("/tp 40 50"));
            Assert.Equal((1002u, 430, 380), await TeleportAsync("/tp TC"));
            Assert.Equal((1006u, 20, 25), await TeleportAsync("/tp stable 20 25"));
            Assert.Equal((ServerFixture.DesertCity, 100, 120), await TeleportAsync("/tp desert 100 120"));
            Assert.Equal((1002u, 600, 520), await TeleportAsync("/tp 1002 600 520"));

            await CommandAsync(gm, "/tp nowhere");
            var places = await NoticeAsync(gm, "No place nowhere");
            Assert.Contains("twin_city (tc)", places?.Text);
        }

        [Fact]
        public async Task ModeratorCanKickButNotTeleport()
        {
            await using var mod = (await this.PlayingAtAsync(1002, 660, 520, Moderator)).Player;
            await using var target = (await this.PlayingAtAsync(1002, 661, 520)).Player;

            await CommandAsync(mod, "/tp 1006 30 30");
            Assert.NotNull(await NoticeAsync(mod, "Unknown command /tp"));

            await CommandAsync(mod, "/kick " + target.User.Name);
            Assert.True(await target.Game.WaitClosedAsync());
            await this.Servers.WaitForEventAsync("game", "session.logout", e =>
                e.TryGetProperty("name", out var name) && name.GetString() == target.User.Name &&
                e.TryGetProperty("reason", out var reason) && reason.GetString() == "kicked");
        }

        [Fact]
        public async Task ModeratorCanBanAndUnban()
        {
            await using var mod = (await this.PlayingAtAsync(1002, 670, 520, Moderator)).Player;
            var (target, email) = await this.PlayingAtAsync(1002, 671, 520);
            await using var _ = target;

            await CommandAsync(mod, $"/ban {target.User.Name} botting");
            Assert.NotNull(await target.Game.WaitForTalkAsync(t => t.Text.Contains("banned: botting")));
            Assert.True(await target.Game.WaitClosedAsync());
            await this.Servers.WaitForEventAsync("game", "session.logout", e =>
                e.TryGetProperty("name", out var name) && name.GetString() == target.User.Name &&
                e.TryGetProperty("reason", out var reason) && reason.GetString() == "banned");
            var logged = await this.Servers.WaitForEventAsync("game", "gm.command", e =>
                e.TryGetProperty("command", out var c) && c.GetString() == "ban" &&
                e.TryGetProperty("name", out var n) && n.GetString() == mod.User.Name);
            Assert.Equal("ok", logged.GetProperty("result").GetString());
            Assert.Equal(Banned, (await this.Servers.LoginAsync(email, Password)).RejectCode);

            await CommandAsync(mod, "/unban " + email);
            Assert.NotNull(await NoticeAsync(mod, "Unbanned account"));
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
        }

        [Fact]
        public async Task ModeratorsCantBanOrMuteTheirEquals()
        {
            await using var mod = (await this.PlayingAtAsync(1002, 672, 520, Moderator)).Player;
            await using var other = (await this.PlayingAtAsync(1002, 673, 520, Moderator)).Player;

            await CommandAsync(mod, "/ban " + other.User.Name);
            Assert.NotNull(await NoticeAsync(mod, "You can't ban"));
            await CommandAsync(mod, $"/mute {other.User.Name} 5");
            Assert.NotNull(await NoticeAsync(mod, "You can't mute"));
        }

        [Fact]
        public async Task MutedPlayersCantChatEvenAfterRelogin()
        {
            await using var mod = (await this.PlayingAtAsync(1002, 674, 520, Moderator)).Player;
            var (target, email) = await this.PlayingAtAsync(1002, 675, 520);

            await CommandAsync(mod, $"/mute {target.User.Name} 30");
            Assert.NotNull(await NoticeAsync(target, "You have been muted"));
            await target.Game.SayAsync(TalkChannels.Talk, target.User.Name, "ALLUSERS", "can you hear me");
            Assert.NotNull(await NoticeAsync(target, "You are muted"));
            await target.Game.SayAsync(TalkChannels.Whisper, target.User.Name, mod.User.Name, "psst");
            await SyncAsync(target);
            Assert.Null(await mod.Game.WaitForTalkAsync(t => t.Sender == target.User.Name, Already));

            await this.LogOutAsync(target);
            await using var again = await this.Servers.PlayAsync(email, Password);
            await again.Game.SayAsync(TalkChannels.Whisper, again.User.Name, mod.User.Name, "still?");
            Assert.NotNull(await NoticeAsync(again, "You are muted"));
            var rejected = await this.Servers.WaitForEventAsync("game", "chat.rejected", "name", again.User.Name);
            Assert.Equal("muted", rejected.GetProperty("reason").GetString());

            await CommandAsync(mod, "/unmute " + again.User.Name);
            Assert.NotNull(await NoticeAsync(again, "You can chat again"));
            await SyncAsync(again, mod);
        }

        [Fact]
        public async Task GameMastersCanAskTheClientToOpenAnyWindow()
        {
            await using var player = (await this.PlayingAtAsync(1002, 800, 940, GameMaster)).Player;
            await CommandAsync(player, "/window 7");
            var opened = await player.Game.WaitForActionAsync(126, player.User.Id);
            Assert.Equal(7u, opened?.Command);

            await CommandAsync(player, "/window 9 custom");
            opened = await player.Game.WaitForActionAsync(160, player.User.Id);
            Assert.Equal(9u, opened?.Command);
        }

        private async Task<Player> PlayingAsClassAtAsync(uint mapID, int x, int y, byte currentClass)
        {
            var (first, email) = await this.PlayingAsync();
            var name = first.User.Name;
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET MapID = @map, X = @x, Y = @y, CurrentClass = @class WHERE Name = @name",
                ("@map", mapID), ("@x", x), ("@y", y), ("@class", currentClass), ("@name", name));
            return await this.Servers.PlayAsync(email, Password);
        }

        private static async Task KillAsync(Player attacker, Player victim, int x, int y)
        {
            await attacker.Game.AttackAsync(attacker.User.Id, victim.User.Id, x, y);
            Assert.NotNull(await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, TimeSpan.FromSeconds(15)));
            await victim.Game.BecomeGhostAsync(victim.User.Id);
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(UserAttribMessage.Mesh, out var m) && m >= 10_000_000));
        }

        [Fact]
        public async Task GhostChatReachesOnlyOtherGhostsAndWaterTaoistsInView()
        {
            uint map = ServerFixture.DesertCity;
            await using var killer = (await this.PlayingAtAsync(map, 140, 120, strength: 5000)).Player;
            await using var speaker = (await this.PlayingAtAsync(map, 141, 120)).Player;
            await using var otherGhost = (await this.PlayingAtAsync(map, 140, 121)).Player;
            await using var waterTaoist = await this.PlayingAsClassAtAsync(map, 142, 120, 132);
            await using var fireTaoist = await this.PlayingAsClassAtAsync(map, 143, 120, 142);
            await using var internTaoist = await this.PlayingAsClassAtAsync(map, 144, 120, 100);
            await using var farWaterTaoist = await this.PlayingAsClassAtAsync(map, 200, 200, 135);

            // An alive player cannot talk on the Ghost channel
            await speaker.Game.SayAsync(TalkChannels.Ghost, speaker.User.Name, "ALLUSERS", "still breathing");
            var rejected = await this.Servers.WaitForEventAsync("game", "chat.rejected", "name", speaker.User.Name);
            Assert.Equal("alive", rejected.GetProperty("reason").GetString());
            await SyncAsync(speaker, waterTaoist);
            Assert.Null(await waterTaoist.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost, Already));

            await FreePkAsync(killer);
            await KillAsync(killer, speaker, 141, 120);
            await KillAsync(killer, otherGhost, 140, 121);

            await speaker.Game.SayAsync(TalkChannels.Ghost, "Impostor", "ALLUSERS", "revive me");

            var heardByGhost = await otherGhost.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost);
            Assert.NotNull(heardByGhost);
            Assert.Equal((speaker.User.Name, "revive me"), (heardByGhost.Sender, heardByGhost.Text));

            var heardByWater = await waterTaoist.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost);
            Assert.NotNull(heardByWater);
            Assert.Equal((speaker.User.Name, "revive me"), (heardByWater.Sender, heardByWater.Text));

            await SyncAsync(speaker, killer);
            await SyncAsync(speaker, fireTaoist);
            await SyncAsync(speaker, internTaoist);
            await SyncAsync(speaker, farWaterTaoist);
            await SyncAsync(speaker);

            Assert.Null(await killer.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost, Already));
            Assert.Null(await fireTaoist.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost, Already));
            Assert.Null(await internTaoist.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost, Already));
            Assert.Null(await farWaterTaoist.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost, Already));
            Assert.Null(await speaker.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Ghost, Already));

            var logged = await this.Servers.WaitForEventAsync("game", "chat.message", e =>
                e.TryGetProperty("name", out var n) && n.GetString() == speaker.User.Name &&
                e.TryGetProperty("channel", out var c) && c.GetString() == "Ghost");
            Assert.Equal("revive me", logged.GetProperty("text").GetString());
        }
    }
}

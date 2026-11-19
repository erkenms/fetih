namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Twin City's NPCs come from the seeded npc table
    public class NpcTests : IntegrationTest
    {
        private const ushort GameMaster = 3;
        private const uint Conductress = 10050, Guard = 30104, Warehouseman = 8, Unscripted = 10002, ArenaKeeper = 10021;

        public NpcTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task NpcsInViewAreShown()
        {
            await using var player = (await this.PlayingAtAsync(1002, 436, 436)).Player;
            var npc = await player.Game.WaitForNpcAsync(Conductress);
            Assert.Equal(((ushort)435, (ushort)440, (ushort)280, (ushort)29), (npc?.X, npc?.Y, npc?.Lookface, npc?.Role));
            Assert.Null(await player.Game.WaitForNpcAsync(Warehouseman, TimeSpan.FromMilliseconds(300)));

            await player.Game.JumpAsync(player.User.Id, 436, 436, 420, 425);
            await player.Game.JumpAsync(player.User.Id, 420, 425, 410, 410);
            await player.Game.JumpAsync(player.User.Id, 410, 410, 410, 395);
            await player.Game.JumpAsync(player.User.Id, 410, 395, 410, 380);
            await player.Game.JumpAsync(player.User.Id, 410, 380, 410, 365);
            Assert.NotNull(await player.Game.WaitForNpcAsync(Warehouseman));
        }

        [Fact]
        public async Task TheConductressTeleportsForAFare()
        {
            await using var player = (await this.PlayingAtAsync(1002, 436, 436)).Player;
            await player.Game.TalkToNpcAsync(Conductress);
            var page = await player.Game.ReadDialogAsync();
            Assert.NotNull(page);
            Assert.Contains("100 silver", page[0].Text);
            Assert.Equal(7, page.Count(p => p.Action == DialogMessage.Choice));
            Assert.Equal((DialogMessage.Face, (byte)1), (page[^2].Action, page[^2].Option));
            Assert.Equal(DialogMessage.Show, page[^1].Action);

            await player.Game.AnswerNpcAsync(2);
            var teleport = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal(((ushort)69, (ushort)473), (teleport?.X, teleport?.Y));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 900));
        }

        [Fact]
        public async Task TheConductressWantsHerFare()
        {
            await using var player = (await this.PlayingAtAsync(1002, 437, 436, GameMaster)).Player;
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/silver -950");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 50));

            await player.Game.TalkToNpcAsync(Conductress);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            var page = await player.Game.ReadDialogAsync();
            Assert.Contains("do not have 100 silver", page?[0].Text);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id, TimeSpan.FromMilliseconds(300)));
        }

        [Fact]
        public async Task NpcsWithoutADialogGiveTheDefaultAnswer()
        {
            await using var player = (await this.PlayingAtAsync(1002, 412, 380)).Player;
            await player.Game.TalkToNpcAsync(Unscripted);
            var page = await player.Game.ReadDialogAsync();
            Assert.Contains("nothing to say", page?[0].Text);

            await player.Game.TalkToNpcAsync(Guard);
            Assert.Null(await player.Game.ReadDialogAsync(TimeSpan.FromMilliseconds(300)));
            var refused = await this.Servers.WaitForEventAsync("game", "npc.rejected", "name", player.User.Name);
            Assert.Equal("too_far", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task TeleportersToMissingMapsKeepTheFare()
        {
            await using var player = (await this.PlayingAtAsync(1002, 459, 295)).Player;
            await player.Game.TalkToNpcAsync(ArenaKeeper);
            var page = await player.Game.ReadDialogAsync();
            Assert.Contains("50 silver", page?[0].Text);
            await player.Game.AnswerNpcAsync(1);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id, TimeSpan.FromMilliseconds(300)));
            Assert.Null(await player.Game.WaitForAttribAsync(a => a.Values.ContainsKey(Silver), TimeSpan.FromMilliseconds(300)));
        }

        [Fact]
        public async Task ClosingTheDialogEndsTheConversation()
        {
            await using var player = (await this.PlayingAtAsync(1002, 438, 436)).Player;
            await player.Game.TalkToNpcAsync(Conductress);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(DialogMessage.NoOption);
            await player.Game.AnswerNpcAsync(1);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id, TimeSpan.FromMilliseconds(500)));
        }
    }
}

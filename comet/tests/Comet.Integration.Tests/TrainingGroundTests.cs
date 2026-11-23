namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class TrainingGroundTests : IntegrationTest
    {
        public TrainingGroundTests(ServerFixture servers) : base(servers) { }

        private const uint Boxer = 101607, BoxerHuang = 600075;
        private static readonly TimeSpan Already = TimeSpan.FromMilliseconds(300);

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
    }
}

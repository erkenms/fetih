namespace Fetih.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    public class NameChangeTests : IntegrationTest
    {
        private const uint NameRegistrar = 7935;
        private const uint ChangeNameWindow = 489;

        public NameChangeTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task NameRegistrarOpensTheChangeNameWindow()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.Market, 305, 217)).Player;
            await player.Game.TalkToNpcAsync(NameRegistrar);
            var page = await player.Game.ReadDialogAsync();
            Assert.NotNull(page);
            Assert.Contains("810 CPs", string.Concat(page.Where(p => p.Action == DialogMessage.Line).Select(p => p.Text)));

            await player.Game.AnswerNpcAsync(1);
            var open = await player.Game.WaitForActionAsync(ActionMessage.OpenWindow, player.User.Id);
            Assert.NotNull(open);
            Assert.Equal(ChangeNameWindow, open.Command);
        }

        [Fact]
        public async Task ChangeNameCharges810CPsOncePersistsAndUpdatesOnRelog()
        {
            var (player, email) = await this.PlayingAtAsync(ServerFixture.Market, 304, 217, GameMaster);
            var other = (await this.PlayingAtAsync(ServerFixture.Market, 303, 217)).Player;
            await using var _other = other;

            // Query before changing: 0 used, 1 remaining
            await player.Game.QueryNameChangeAsync();
            var query = await player.Game.WaitForNameChangeAsync();
            Assert.NotNull(query);
            Assert.Equal(ChangeNameMessage.Query, query.Action);
            Assert.Equal((ushort)0, query.UsedCount);
            Assert.Equal((ushort)1, query.RemainingCount);

            // Refusal: invalid name
            await player.Game.ChangeNameAsync("ab");
            var invalidEvt = await this.Servers.WaitForEventAsync("game", "character.rename_rejected",
                e => e.GetProperty("name").GetString() == player.User.Name &&
                     e.GetProperty("reason").GetString() == "invalid_name");
            Assert.Equal("invalid_name", invalidEvt.GetProperty("reason").GetString());

            // Refusal: not enough CPs
            string freshName = "Ren" + Guid.NewGuid().ToString("N")[..8];
            await player.Game.ChangeNameAsync(freshName);
            var noCpsEvt = await this.Servers.WaitForEventAsync("game", "character.rename_rejected",
                e => e.GetProperty("name").GetString() == player.User.Name &&
                     e.GetProperty("reason").GetString() == "no_cps");
            Assert.Equal("no_cps", noCpsEvt.GetProperty("reason").GetString());

            // Grant 1000 CPs
            await CommandAsync(player, "/jewel 1000");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Id == player.User.Id && a.Values.TryGetValue(Jewels, out var j) && j == 1000));

            // Refusal: duplicate name
            await player.Game.ChangeNameAsync(other.User.Name);
            var dup = await player.Game.WaitForNameChangeAsync();
            Assert.NotNull(dup);
            Assert.Equal(ChangeNameMessage.Duplicate, dup.Action);

            // Success
            await player.Game.ChangeNameAsync(freshName);
            var ok = await player.Game.WaitForNameChangeAsync();
            Assert.NotNull(ok);
            Assert.Equal(ChangeNameMessage.Success, ok.Action);
            var cpsAfter = await player.Game.WaitForAttribAsync(a =>
                a.Id == player.User.Id && a.Values.TryGetValue(Jewels, out var j) && j == 190);
            Assert.NotNull(cpsAfter);

            // Query after changing: 1 used, 0 remaining
            await player.Game.QueryNameChangeAsync();
            var queryAfter = await player.Game.WaitForNameChangeAsync();
            Assert.NotNull(queryAfter);
            Assert.Equal((ushort)1, queryAfter.UsedCount);
            Assert.Equal((ushort)0, queryAfter.RemainingCount);

            // Refusal: already used
            await player.Game.ChangeNameAsync("Ren" + Guid.NewGuid().ToString("N")[..8]);
            var closed = await player.Game.WaitForNameChangeAsync();
            Assert.NotNull(closed);
            Assert.Equal(ChangeNameMessage.Close, closed.Action);

            // Relogging shows the new name (logout saves under the new name)
            await player.DisposeAsync();
            await this.Servers.WaitForEventAsync("game", "character.saved", "name", freshName);
            await using var relogged = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(freshName, relogged.User.Name);
            Assert.Equal(1L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT NameChanged FROM `character` WHERE CharacterID = @id", ("@id", relogged.User.Id))));
        }
    }
}

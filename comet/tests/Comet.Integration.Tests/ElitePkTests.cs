namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    // The Elite PK Tournament of the under-100 group, opened and started by a game master
    public class ElitePkTests : IntegrationTest
    {
        private const uint Envoy = 7879, TwinCity = 1002;
        private const uint WaitingRoom = ServerFixture.ElitePkWaitingRoom, Arena = ServerFixture.Arena;
        private static readonly TimeSpan Countdown = TimeSpan.FromSeconds(ServerFixture.ElitePkCountdownSeconds);

        public ElitePkTests(ServerFixture servers) : base(servers) { }

        private static async Task SignUpAsync(Player player)
        {
            await player.Game.TalkToNpcAsync(Envoy);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.Equal(WaitingRoom, (await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id))?.Command);
        }

        [Fact]
        public async Task TwoPlayersFightTheFinalAndTheChampionClaimsThePrize()
        {
            var (a, _) = await this.PlayingAtAsync(TwinCity, 431, 249, GameMaster, level: 50, strength: 5000);
            await using var _a = a;
            var (b, _) = await this.PlayingAtAsync(TwinCity, 433, 249, GameMaster, level: 50);
            await using var _b = b;

            await CommandAsync(a, "/elitepk open 0");
            await this.Servers.WaitForEventAsync("game", "elitepk.opened", e => e.GetProperty("group").GetInt32() == 0);
            await SignUpAsync(a);
            await SignUpAsync(b);

            // Two players go straight to the final: 5 minutes, in an arena of their own
            await CommandAsync(a, "/elitepk start 0");
            ushort bx = 0, by = 0;
            foreach (var player in new[] { a, b })
            {
                var arrived = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
                Assert.Equal(Arena, arrived?.Command);
                if (player == b)
                    (bx, by) = (arrived.X, arrived.Y);
                var start = await player.Game.WaitForElitePkAsync(m => m.Action == ElitePkArenic.Start);
                Assert.Equal((ushort)300, start?.Seconds);
            }

            await Task.Delay(Countdown + TimeSpan.FromMilliseconds(500));
            await a.Game.AttackAsync(a.User.Id, b.User.Id, bx, by);
            var kill = await b.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == b.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(a.User.Id, kill?.Sender);
            Assert.NotNull(await b.Game.WaitForElitePkScoreAsync(s => s.DamageA > 0 || s.DamageB > 0));
            Assert.True((await a.Game.WaitForElitePkAsync(m => m.Action == ElitePkArenic.Result))?.Won);
            Assert.False((await b.Game.WaitForElitePkAsync(m => m.Action == ElitePkArenic.Result))?.Won);
            var ended = await this.Servers.WaitForEventAsync("game", "elitepk.match_ended", "winner", a.User.Name);
            Assert.Equal("killed", ended.GetProperty("reason").GetString());

            // Each fighter gets the match's EXP pack and honor, then goes home by the Envoy
            foreach (var player in new[] { a, b })
            {
                Assert.NotNull(await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.ElitePkExpPack.Id));
                var home = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
                Assert.Equal(TwinCity, home?.Command);
            }
            await this.Servers.WaitForEventAsync("game", "elitepk.ended", e => e.GetProperty("group").GetInt32() == 0);
            Assert.Equal(1000u, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT HonorPoints FROM arena WHERE CharacterID = @id", ("@id", b.User.Id))));
            Assert.Equal(2, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT `Rank` FROM elite_pk_reward WHERE CharacterID = @id", ("@id", b.User.Id))));

            // The champion's prize: the Refined (under 100) Champion Pack
            await a.Game.TalkToNpcAsync(Envoy);
            Assert.NotNull(await a.Game.ReadDialogAsync());
            await a.Game.AnswerNpcAsync(2);
            Assert.NotNull(await a.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.ElitePkChampionPack.Id));
            await this.Servers.WaitForEventAsync("game", "elitepk.reward_claimed", "name", a.User.Name);
            Assert.True(Convert.ToBoolean(await this.Servers.QueryGameScalarAsync(
                "SELECT Claimed FROM elite_pk_reward WHERE CharacterID = @id", ("@id", a.User.Id))));
        }
    }
}

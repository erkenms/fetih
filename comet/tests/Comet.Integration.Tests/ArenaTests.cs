namespace Comet.Integration.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.QualifierMessage;

    // The arena qualifier: two level 50 players in Desert City sign up, are matched and fight
    // in the arena (900000); a level 50 player starts with 1500 arena points
    public class ArenaTests : IntegrationTest, IAsyncLifetime
    {
        private const uint DesertCity = ServerFixture.DesertCity, Arena = ServerFixture.Arena;
        private const uint StartingPoints = 1500;
        private static readonly TimeSpan CountdownTime = TimeSpan.FromSeconds(ServerFixture.ArenaCountdownSeconds);

        private readonly List<Player> Fighters = new List<Player>();

        public ArenaTests(ServerFixture servers) : base(servers) { }

        public Task InitializeAsync() => Task.CompletedTask;

        // The queue is the whole server's: a fighter left signed up, say by a failed test,
        // would be matched in the next one. Quitting is answered once they're off it.
        public async Task DisposeAsync()
        {
            foreach (var fighter in this.Fighters)
            {
                // A connection the server closed has left the queue with its logout
                try
                {
                    await fighter.Game.SendAsync(Build(Quit));
                    await fighter.Game.WaitForQualifierAsync(q => q.Action == Quit, TimeSpan.FromSeconds(2));
                }
                catch (System.IO.IOException) { }
                await this.LogOutAsync(fighter);
            }
        }

        private async Task<Player> FighterAsync(int x, ushort strength = 0)
        {
            var fighter = (await this.PlayingAtAsync(DesertCity, x, 300 - 50, level: 50, strength: strength)).Player;
            this.Fighters.Add(fighter);
            return fighter;
        }

        private static async Task SignUpAsync(Player player)
        {
            await player.Game.SendAsync(Build(SignUp));
            Assert.NotNull(await player.Game.WaitForQualifierAsync(q => q.Action == SignUp));
        }

        // Both sign up, are asked and accept; true once both are in the arena
        private static async Task MatchAsync(Player a, Player b)
        {
            await SignUpAsync(a);
            Assert.NotNull(await a.Game.WaitForQualifierDetailsAsync(d => d.Status == 1 && d.Points == StartingPoints));
            await SignUpAsync(b);
            Assert.Equal(b.User.Id, (await a.Game.WaitForQualifierAsync(q => q.Action == Countdown))?.Id);
            Assert.Equal(a.User.Id, (await b.Game.WaitForQualifierAsync(q => q.Action == Countdown))?.Id);
            Assert.NotNull(await a.Game.WaitForQualifierDetailsAsync(d => d.Status == 2));

            await a.Game.SendAsync(Build(Answer, Accept));
            await b.Game.SendAsync(Build(Answer, Accept));
            foreach (var (player, opponent) in new[] { (a, b), (b, a) })
            {
                Assert.Equal(Arena, (await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id))?.Command);
                var start = await player.Game.WaitForQualifierAsync(q => q.Action == StartFight);
                Assert.Equal((opponent.User.Id, opponent.User.Name), (start?.Id, start?.Name));
                Assert.Equal(Match, (await player.Game.WaitForQualifierAsync(q => q.Action == Match))?.Action);
            }
        }

        [Fact]
        public async Task TheWinnerTakesArenaPointsAndBothGoHome()
        {
            var a = await this.FighterAsync(100, strength: 5000);
            var b = await this.FighterAsync(110);
            await MatchAsync(a, b);

            // In the arena the fighters stand a step apart, at 49,50 and 51,50; nobody may
            // attack during the countdown
            await a.Game.AttackAsync(a.User.Id, b.User.Id, 51, 50);
            var refused = await this.Servers.WaitForEventAsync("game", "combat.rejected", "name", a.User.Name);
            Assert.Equal("pk_mode", refused.GetProperty("reason").GetString());

            var matches = await this.QueryMatchesAsync(a);
            Assert.Contains(matches.Shown, m => m == (a.User.Id, b.User.Id) || m == (b.User.Id, a.User.Id));

            await Task.Delay(CountdownTime);
            await a.Game.AttackAsync(a.User.Id, b.User.Id, 51, 50);
            var kill = await b.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == b.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(a.User.Id, kill?.Sender);
            Assert.NotNull(await b.Game.WaitForArenaScoreAsync(s => s.IdA == a.User.Id && s.DamageA > 0));
            Assert.Equal(Won, (await a.Game.WaitForQualifierAsync(q => q.Action == Dialog))?.Option);
            Assert.Equal(Lost, (await b.Game.WaitForQualifierAsync(q => q.Action == Dialog))?.Option);

            var ended = await this.Servers.WaitForEventAsync("game", "arena.ended", "winner", a.User.Name);
            Assert.Equal("killed", ended.GetProperty("reason").GetString());
            Assert.Equal(StartingPoints * 3 / 100, ended.GetProperty("points").GetUInt32());

            // Home after the match, the loser alive again
            foreach (var (player, x, won) in new[] { (a, 100, 1u), (b, 110, 0u) })
            {
                var home = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
                Assert.Equal((DesertCity, (ushort)x), (home?.Command, home?.X));
                Assert.Equal(won, (await player.Game.WaitForQualifierAsync(q => q.Action == EndDialog))?.Option);
            }
            await this.Servers.WaitForEventAsync("game", "character.revived", e =>
                e.GetProperty("name").GetString() == b.User.Name && e.GetProperty("reason").GetString() == "qualifier");

            var winner = await a.Game.WaitForQualifierDetailsAsync(d => d.Status == 0 && d.WinsToday == 1);
            Assert.Equal((StartingPoints + 45, 1u, 0u), (winner?.Points, winner?.WinsTotal, winner?.LossesTotal));
            var loser = await b.Game.WaitForQualifierDetailsAsync(d => d.Status == 0 && d.LossesToday == 1);
            Assert.Equal(StartingPoints - 45, loser?.Points);
            Assert.Equal(StartingPoints + 45, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Points FROM arena WHERE CharacterID = @id", ("@id", a.User.Id))));

            await a.Game.SendAsync(QualifierMessage.BuildRankQuery(0, 1));
            var ranking = await a.Game.WaitForQualifierRankingAsync();
            Assert.Contains(ranking.Shown, r => r.Name == a.User.Name && r.Points == StartingPoints + 45);
        }

        [Fact]
        public async Task GivingUpTheInvitationLosesTheMatch()
        {
            var a = await this.FighterAsync(120);
            var b = await this.FighterAsync(130);
            await SignUpAsync(a);
            await SignUpAsync(b);
            Assert.NotNull(await b.Game.WaitForQualifierAsync(q => q.Action == Countdown));

            await b.Game.SendAsync(Build(Answer, GiveUp));
            Assert.NotNull(await a.Game.WaitForQualifierAsync(q => q.Action == OpponentGaveUp));
            Assert.Equal(Won, (await a.Game.WaitForQualifierAsync(q => q.Action == EndDialog))?.Option);
            Assert.Equal(0u, (await b.Game.WaitForQualifierAsync(q => q.Action == EndDialog))?.Option);
            var ended = await this.Servers.WaitForEventAsync("game", "arena.ended", "winner", a.User.Name);
            Assert.Equal("gave_up", ended.GetProperty("reason").GetString());
            Assert.NotNull(await b.Game.WaitForQualifierDetailsAsync(d => d.Status == 0 && d.LossesToday == 1));
        }

        [Fact]
        public async Task WhoAcceptsAloneWinsWhenTimeIsUp()
        {
            var a = await this.FighterAsync(140);
            var b = await this.FighterAsync(150);
            await SignUpAsync(a);
            await SignUpAsync(b);
            Assert.NotNull(await a.Game.WaitForQualifierAsync(q => q.Action == Countdown));
            await a.Game.SendAsync(Build(Answer, Accept));

            Assert.Equal(Won, (await a.Game.WaitForQualifierAsync(
                q => q.Action == EndDialog, TimeSpan.FromSeconds(ServerFixture.ArenaInviteSeconds + 5)))?.Option);
            var ended = await this.Servers.WaitForEventAsync("game", "arena.ended", "winner", a.User.Name);
            Assert.Equal("no_answer", ended.GetProperty("reason").GetString());
            await SyncAsync(a);
            Assert.Null(await a.Game.WaitForActionAsync(ActionMessage.Teleport, a.User.Id, Already));
        }

        [Fact]
        public async Task LoggingOutLosesTheMatch()
        {
            var a = await this.FighterAsync(160);
            var b = await this.FighterAsync(170);
            await MatchAsync(a, b);

            var loser = b.User.Name;
            this.Fighters.Remove(b);
            await b.DisposeAsync();
            Assert.Equal(Won, (await a.Game.WaitForQualifierAsync(q => q.Action == Dialog))?.Option);
            var ended = await this.Servers.WaitForEventAsync("game", "arena.ended", "winner", a.User.Name);
            Assert.Equal(("logout", loser), (ended.GetProperty("reason").GetString(), ended.GetProperty("loser").GetString()));
            Assert.Equal(DesertCity, (await a.Game.WaitForActionAsync(ActionMessage.Teleport, a.User.Id))?.Command);
        }

        [Fact]
        public async Task OnlyLevel40AndUpMaySignUp()
        {
            await using var player = (await this.PlayingAtAsync(DesertCity, 180, 250, level: 39)).Player;
            await player.Game.SendAsync(Build(SignUp));
            var refused = await this.Servers.WaitForEventAsync("game", "arena.rejected", "name", player.User.Name);
            Assert.Equal("level", refused.GetProperty("reason").GetString());
            Assert.NotNull(await player.Game.WaitForQualifierDetailsAsync(d => d.Status == 0 && d.Points == 0));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForQualifierAsync(q => q.Action == SignUp, Already));
        }

        private async Task<QualifierMatches> QueryMatchesAsync(Player player)
        {
            await player.Game.SendAsync(QualifierMessage.BuildFightersQuery(1));
            return await player.Game.WaitForQualifierMatchesAsync();
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    // The Team PK Tournament of the under-100 group, opened and started by a game master: a
    // team of two against a team of one
    public class TeamPkTests : IntegrationTest
    {
        private const uint Manager = 8158, TwinCity = 1002;
        private const uint WaitingRoom = ServerFixture.ElitePkWaitingRoom, Arena = ServerFixture.Arena;
        private static readonly TimeSpan Countdown = TimeSpan.FromSeconds(ServerFixture.ElitePkCountdownSeconds);

        public TeamPkTests(ServerFixture servers) : base(servers) { }

        private static async Task CreateTeamAsync(Player leader)
        {
            await leader.Game.TeamAsync(TeamMessage.Create, leader.User.Id);
            Assert.NotNull(await leader.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Create && t.Data == leader.User.Id));
        }

        private static async Task InviteAsync(Player leader, Player member)
        {
            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id));
            await leader.Game.TeamAsync(TeamMessage.Invite, member.User.Id);
            Assert.NotNull(await member.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Invite && t.Data == leader.User.Id));
            await member.Game.TeamAsync(TeamMessage.AcceptInvite, leader.User.Id);
            Assert.NotNull(await member.Game.WaitForTeamMemberAsync(m => m.Id == leader.User.Id));
        }

        private static async Task SignUpAsync(Player player)
        {
            await player.Game.TalkToNpcAsync(Manager);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.Equal(WaitingRoom, (await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id))?.Command);
        }

        [Fact]
        public async Task TwoTeamsFightTheFinalAndTheChampionsLeaderClaimsThePrize()
        {
            var (a, _) = await this.PlayingAtAsync(TwinCity, 440, 249, GameMaster, level: 50, strength: 5000);
            await using var _a = a;
            var (c, _) = await this.PlayingAtAsync(TwinCity, 441, 249, level: 50);
            await using var _c = c;
            var (b, _) = await this.PlayingAtAsync(TwinCity, 442, 249, level: 50);
            await using var _b = b;
            await CreateTeamAsync(a);
            await InviteAsync(a, c);
            await CreateTeamAsync(b);

            await CommandAsync(a, "/teampk open 0");
            await this.Servers.WaitForEventAsync("game", "teampk.opened", e => e.GetProperty("group").GetInt32() == 0);
            // The member can't sign up before the leader
            await c.Game.TalkToNpcAsync(Manager);
            Assert.NotNull(await c.Game.ReadDialogAsync());
            await c.Game.AnswerNpcAsync(1);
            await this.Servers.WaitForEventAsync("game", "teampk.rejected", "reason", "leader_first");
            await SignUpAsync(a);
            await SignUpAsync(c);
            await SignUpAsync(b);

            // Two teams go straight to the final: 15 minutes, in an arena of their own
            await CommandAsync(a, "/teampk start 0");
            ushort bx = 0, by = 0;
            foreach (var player in new[] { a, c, b })
            {
                var arrived = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
                Assert.Equal(Arena, arrived?.Command);
                if (player == b)
                    (bx, by) = (arrived.X, arrived.Y);
                var start = await player.Game.WaitForTeamPkAsync(m => m.Action == TeamPkArenic.Start);
                Assert.Equal((ushort)900, start?.Seconds);
            }

            // The other team's lone fighter falls, and the team is wiped out
            await Task.Delay(Countdown + TimeSpan.FromMilliseconds(500));
            await a.Game.AttackAsync(a.User.Id, b.User.Id, bx, by);
            var kill = await b.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == b.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(a.User.Id, kill?.Sender);
            var score = await c.Game.WaitForTeamPkScoreAsync(s => s.ScoreA > 0 || s.ScoreB > 0);
            Assert.Equal(a.User.Id, score.ScoreA > 0 ? score.TeamA : score.TeamB);
            Assert.True((await a.Game.WaitForTeamPkAsync(m => m.Action == TeamPkArenic.Result))?.Won);
            Assert.True((await c.Game.WaitForTeamPkAsync(m => m.Action == TeamPkArenic.Result))?.Won);
            Assert.False((await b.Game.WaitForTeamPkAsync(m => m.Action == TeamPkArenic.Result))?.Won);
            var ended = await this.Servers.WaitForEventAsync("game", "teampk.match_ended", "winner", "Team of " + a.User.Name);
            Assert.Equal("wiped", ended.GetProperty("reason").GetString());

            // Every fighter gets the match's EXP pack and honor, then goes home
            foreach (var player in new[] { a, c, b })
            {
                Assert.NotNull(await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.TeamPkPack.Id));
                var home = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
                Assert.Equal(TwinCity, home?.Command);
            }
            await this.Servers.WaitForEventAsync("game", "teampk.ended", e => e.GetProperty("group").GetInt32() == 0);
            Assert.Equal(1000u, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT HonorPoints FROM arena WHERE CharacterID = @id", ("@id", c.User.Id))));
            Assert.Equal(2, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT `Rank` FROM team_pk_reward WHERE Event = 0 AND LeaderID = @id", ("@id", b.User.Id))));

            // Only the leader claims the champions' Refined Team PK Champion Pack
            await a.Game.TalkToNpcAsync(Manager);
            Assert.NotNull(await a.Game.ReadDialogAsync());
            await a.Game.AnswerNpcAsync(2);
            Assert.NotNull(await a.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.TeamPkChampionPack.Id));
            await this.Servers.WaitForEventAsync("game", "teampk.reward_claimed", "name", a.User.Name);
            Assert.True(Convert.ToBoolean(await this.Servers.QueryGameScalarAsync(
                "SELECT Claimed FROM team_pk_reward WHERE Event = 0 AND LeaderID = @id", ("@id", a.User.Id))));
        }
    }
}

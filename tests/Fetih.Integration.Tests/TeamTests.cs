namespace Fetih.Integration.Tests
{
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class TeamTests : IntegrationTest
    {
        public TeamTests(ServerFixture servers) : base(servers) { }

        private static async Task CreateAsync(Player leader)
        {
            await leader.Game.TeamAsync(TeamMessage.Create, leader.User.Id);
            Assert.NotNull(await leader.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Create && t.Data == leader.User.Id));
        }

        private static async Task InviteAsync(Player leader, Player member)
        {
            // Both must be on the map, which each one's last login step does in parallel
            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id));
            await leader.Game.TeamAsync(TeamMessage.Invite, member.User.Id);
            Assert.NotNull(await member.Game.WaitForRelationAsync(r => r.AskerId == leader.User.Id));
            Assert.NotNull(await member.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Invite && t.Data == leader.User.Id));
            await member.Game.TeamAsync(TeamMessage.AcceptInvite, leader.User.Id);
            var joined = await leader.Game.WaitForTeamMemberAsync(m => m.Id == member.User.Id);
            Assert.Equal(member.User.Name, joined?.Name);
            Assert.NotNull(await member.Game.WaitForTeamMemberAsync(m => m.Id == leader.User.Id));
        }

        [Fact]
        public async Task PlayersJoinTalkAndLeaveATeam()
        {
            await using var leader = (await this.PlayingAtAsync(1002, 200, 240)).Player;
            await using var member = (await this.PlayingAtAsync(1002, 201, 240)).Player;
            await CreateAsync(leader);
            await this.Servers.WaitForEventAsync("game", "team.created", "name", leader.User.Name);
            await InviteAsync(leader, member);

            await member.Game.SayAsync(TalkChannels.Team, member.User.Name, "ALLUSERS", "hello team");
            Assert.NotNull(await leader.Game.WaitForTalkAsync(t => t.Text == "hello team"));

            await leader.Game.TeamAsync(TeamMessage.Kick, member.User.Id);
            Assert.NotNull(await member.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Kick && t.Data == member.User.Id));

            // Asking to join instead of being invited
            await member.Game.TeamAsync(TeamMessage.ApplyJoin, leader.User.Id);
            Assert.NotNull(await leader.Game.WaitForTeamAsync(t => t.Action == TeamMessage.ApplyJoin && t.Data == member.User.Id));
            await leader.Game.TeamAsync(TeamMessage.AcceptJoin, member.User.Id);
            Assert.NotNull(await member.Game.WaitForTeamMemberAsync(m => m.Id == leader.User.Id));

            await member.Game.TeamAsync(TeamMessage.Leave, 0);
            Assert.NotNull(await leader.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Leave && t.Data == member.User.Id));
            await leader.Game.TeamAsync(TeamMessage.Dismiss, 0);
            Assert.NotNull(await leader.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Dismiss));
            await this.Servers.WaitForEventAsync("game", "team.dismissed", "name", leader.User.Name);
        }

        [Fact]
        public async Task OnlyTheLeaderInvitesAndTheTeamEndsWhenTheLeaderLeaves()
        {
            var leader = (await this.PlayingAtAsync(1002, 210, 240)).Player;
            await using var member = (await this.PlayingAtAsync(1002, 211, 240)).Player;
            await using var other = (await this.PlayingAtAsync(1002, 212, 240)).Player;
            await CreateAsync(leader);
            await InviteAsync(leader, member);

            await member.Game.TeamAsync(TeamMessage.Invite, other.User.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "team.rejected", "name", member.User.Name);
            Assert.Equal("not_leader", refused.GetProperty("reason").GetString());

            await leader.DisposeAsync();
            Assert.NotNull(await member.Game.WaitForTeamAsync(t => t.Action == TeamMessage.Dismiss && t.Data == leader.User.Id));
            var ended = await this.Servers.WaitForEventAsync("game", "team.dismissed", "name", leader.User.Name);
            Assert.Equal("logout", ended.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task TeammatesNearbyShareTheKillBonus()
        {
            var at = ServerFixture.PheasantForTeam;
            await using var leader = (await this.PlayingAtAsync(1002, at.X + 1, at.Y, level: 100)).Player;
            await using var member = (await this.PlayingAtAsync(1002, at.X + 3, at.Y)).Player;
            await CreateAsync(leader);
            await InviteAsync(leader, member);

            var pheasant = await leader.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await leader.Game.AttackAsync(leader.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await leader.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id));

            var shared = await this.Servers.WaitForEventAsync("game", "team.experience", "name", member.User.Name);
            Assert.Equal((leader.User.Name, 1), (shared.GetProperty("killer").GetString(), shared.GetProperty("experience").GetInt32()));
            Assert.NotNull(await member.Game.WaitForAttribAsync(a => a.Values.ContainsKey(UserAttribMessage.Experience)));
        }
    }
}

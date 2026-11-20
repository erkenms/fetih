namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class ClanTests : IntegrationTest
    {
        private const uint ClanManager = 5533;
        private const ushort GameMaster = 3;
        private const uint Silver = 4;

        public ClanTests(ServerFixture servers) : base(servers) { }

        private static string NewClanName() => "C" + Guid.NewGuid().ToString("N")[..8];

        // A level 50 game master near the clan manager, with the silver to found a clan
        private async Task<(Player Player, string Email)> FounderAsync(int x, int y)
        {
            var (player, email) = await this.PlayingAtAsync(1002, x, y, GameMaster, level: 50);
            await player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", "/silver 500000");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 501_000));
            return (player, email);
        }

        private static async Task<uint> CreateAsync(Player leader, string name)
        {
            await leader.Game.TalkToNpcAsync(ClanManager);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(1);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(10, name);

            var info = await leader.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Query);
            Assert.Equal((name, leader.User.Name), (info?.Strings[1], info?.Strings[2]));
            Assert.Equal(new[] { info.Data.ToString(), "1", "0", "250000", "1", "100" }, info.Text.Split(' ').Take(6));
            Assert.NotNull(await leader.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_000));
            return info.Data;
        }

        [Fact]
        public async Task PlayersFoundJoinTalkDonateAndLeaveAClan()
        {
            var name = NewClanName();
            var (founder, founderEmail) = await this.FounderAsync(404, 393);
            await using var member = (await this.PlayingAtAsync(1002, 405, 393)).Player;
            var clanID = await CreateAsync(founder, name);
            await this.Servers.WaitForEventAsync("game", "clan.created", "name", founder.User.Name);

            await founder.Game.FamilyAsync(FamilyMessage.QueryOccupy, 0);
            Assert.NotNull(await founder.Game.WaitForFamilyAsync(f =>
                f.Action == FamilyMessage.Query && f.Data == clanID));
            var occupation = await founder.Game.WaitForFamilyAsync(f =>
                f.Action == FamilyMessage.QueryOccupy && f.Data == clanID);
            Assert.Equal("0 0 0 0 0 0 0 0", occupation?.Text);

            // Invited by the leader
            Assert.NotNull(await founder.Game.WaitForSpawnAsync(member.User.Id));
            await founder.Game.FamilyAsync(FamilyMessage.Recruit, member.User.Id);
            var invite = await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Recruit);
            Assert.Equal((founder.User.Id, name, founder.User.Name), (invite?.Data, invite?.Strings[0], invite?.Strings[1]));
            await member.Game.FamilyAsync(FamilyMessage.AcceptRecruit, founder.User.Id);
            var joined = await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Query);
            Assert.Equal((clanID, name), (joined?.Data, joined?.Strings[1]));
            Assert.NotNull(await founder.Game.WaitForSpawnAsync(member.User.Id,
                s => s.ClanId == clanID && s.ClanRank == FamilyMessage.Member && s.ClanName == name));
            var whisper = await founder.Game.AskWhisperDetailsAsync(member.User.Name);
            Assert.Contains($"#{name}", whisper?.Strings[1]);
            await this.Servers.WaitForEventAsync("game", "clan.joined", "name", member.User.Name);

            await member.Game.SayAsync(TalkChannels.Clan, member.User.Name, "ALLUSERS", "hello clan");
            Assert.NotNull(await founder.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Clan && t.Text == "hello clan"));

            await member.Game.FamilyAsync(FamilyMessage.QueryMembers, 0);
            var members = await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.QueryMembers);
            Assert.Equal(new[] { (founder.User.Name, FamilyMessage.Leader, true), (member.User.Name, FamilyMessage.Member, true) },
                members.Members.Select(m => (m.Name, m.Rank, m.Online)).OrderByDescending(m => m.Item2));

            await member.Game.FamilyAsync(FamilyMessage.Donate, 100);
            var donated = await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Query && f.Text.EndsWith(" 100"));
            Assert.Equal("250100", donated?.Text.Split(' ')[3]);

            await member.Game.FamilyAsync(FamilyMessage.Quit, 0);
            Assert.NotNull(await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Quit && f.Data == clanID));
            var left = await this.Servers.WaitForEventAsync("game", "clan.left", "name", member.User.Name);
            Assert.Equal("left", left.GetProperty("reason").GetString());

            // Asking the leader to join, then being expelled
            await member.Game.FamilyAsync(FamilyMessage.Join, founder.User.Id);
            var asked = await founder.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Join);
            Assert.Equal((member.User.Id, member.User.Name), (asked?.Data, asked?.Text));
            await founder.Game.FamilyAsync(FamilyMessage.AcceptJoin, member.User.Id);
            Assert.NotNull(await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Query && f.Data == clanID));
            await founder.Game.FamilyAsync(FamilyMessage.Kick, 0, member.User.Name);
            Assert.NotNull(await member.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Quit && f.Data == clanID));

            // The clan outlives the leader's session
            await founder.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(founder.User.Name);
            await using var again = await this.Servers.PlayAsync(founderEmail, Password);
            Assert.Equal((clanID, name), (again.Game.Clan?.Data, again.Game.Clan?.Strings[1]));
        }

        [Fact]
        public async Task LeadersAllyTheirClansAndOthersCanBeEnemies()
        {
            await using var first = (await this.FounderAsync(407, 393)).Player;
            await using var second = (await this.FounderAsync(408, 393)).Player;
            var firstName = NewClanName();
            var secondName = NewClanName();
            var firstID = await CreateAsync(first, firstName);
            var secondID = await CreateAsync(second, secondName);

            await first.Game.FamilyAsync(FamilyMessage.AddAlly, second.User.Id);
            var asked = await second.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.AddAlly);
            Assert.Equal((firstID, firstName, first.User.Name), (asked?.Data, asked?.Strings[0], asked?.Strings[1]));
            await second.Game.FamilyAsync(FamilyMessage.AcceptAlly, 0, first.User.Name);
            Assert.NotNull(await first.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.AllyList &&
                f.Clans.Contains((secondID, secondName))));
            Assert.NotNull(await second.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.AllyList &&
                f.Clans.Contains((firstID, firstName))));

            await first.Game.FamilyAsync(FamilyMessage.AddEnemy, 0, secondName);
            var refused = await this.Servers.WaitForEventAsync("game", "clan.rejected", "name", first.User.Name);
            Assert.Equal("ally", refused.GetProperty("reason").GetString());
            await first.Game.FamilyAsync(FamilyMessage.DeleteAlly, 0, secondName);
            Assert.NotNull(await second.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.AllyList && f.Clans.Count == 0));
            await first.Game.FamilyAsync(FamilyMessage.AddEnemy, 0, secondName);
            Assert.NotNull(await first.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.EnemyList &&
                f.Clans.Contains((secondID, secondName))));
        }
    }
}

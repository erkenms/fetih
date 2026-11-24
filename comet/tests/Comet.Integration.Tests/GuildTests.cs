namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class GuildTests : IntegrationTest
    {
        private const uint GuildDirector = 10003;
        private const uint Silver = 4;
        private const uint Leader = 1000, Member = 200;

        public GuildTests(ServerFixture servers) : base(servers) { }

        private static string NewGuildName() => "G" + Guid.NewGuid().ToString("N")[..8];

        // A level 90 game master near the guild director, with the silver to found a guild
        private async Task<(Player Player, string Email)> FounderAsync(int x, int y)
        {
            var (player, email) = await this.PlayingAtAsync(1002, x, y, GameMaster, level: 90);
            await CommandAsync(player, "/silver 1000000");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_001_000));
            return (player, email);
        }

        private static async Task<uint> CreateAsync(Player leader, string name)
        {
            await leader.Game.TalkToNpcAsync(GuildDirector);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(1);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(10, name);

            var info = await leader.Game.WaitForGuildInfoAsync(i => i.GuildId != 0);
            Assert.Equal((Leader, 500_000ul, 1u, leader.User.Name), (info?.Rank, info?.Fund, info?.Members, info?.Leader));
            Assert.NotNull(await leader.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_000));
            return info.GuildId;
        }

        private static async Task RecruitAsync(Player leader, Player member, uint guildID)
        {
            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id));
            await leader.Game.SyndicateAsync(SyndicateMessage.InviteJoin, member.User.Id);
            Assert.NotNull(await member.Game.WaitForSyndicateAsync(s =>
                s.Action == SyndicateMessage.InviteJoin && s.Data == leader.User.Id));
            await member.Game.SyndicateAsync(SyndicateMessage.ApplyJoin, leader.User.Id);
            var info = await member.Game.WaitForGuildInfoAsync(i => i.GuildId == guildID);
            Assert.Equal((Member, 2u), (info?.Rank, info?.Members));
            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id, s => s.GuildId == guildID && s.GuildRank == Member));
        }

        [Fact]
        public async Task PlayersFoundJoinTalkDonateAndLeaveAGuild()
        {
            var name = NewGuildName();
            var (founder, founderEmail) = await this.FounderAsync(398, 396);
            await using var member = (await this.PlayingAtAsync(1002, 399, 396)).Player;
            var guildID = await CreateAsync(founder, name);
            await this.Servers.WaitForEventAsync("game", "guild.created", "name", founder.User.Name);
            await RecruitAsync(founder, member, guildID);
            await this.Servers.WaitForEventAsync("game", "guild.joined", "name", member.User.Name);
            var whisper = await founder.Game.AskWhisperDetailsAsync(member.User.Name);
            Assert.Contains($"#{name}", whisper?.Strings[1]);

            await member.Game.SayAsync(TalkChannels.Guild, member.User.Name, "ALLUSERS", "hello guild");
            Assert.NotNull(await founder.Game.WaitForTalkAsync(t => t.Channel == TalkChannels.Guild && t.Text == "hello guild"));

            var members = await member.Game.QueryGuildMembersAsync();
            Assert.Equal(new[] { (founder.User.Name, Leader, true), (member.User.Name, Member, true) },
                members.Select(m => (m.Name, m.Rank, m.Online)));

            await member.Game.SyndicateAsync(SyndicateMessage.DonateSilver, 100);
            var donated = await member.Game.WaitForGuildInfoAsync(i => i.Donation == 100);
            Assert.Equal(500_100ul, donated?.Fund);

            await member.Game.SyndicateAsync(SyndicateMessage.QueryName, guildID);
            var named = await member.Game.WaitForNameAsync(n => n.Action == NameMessage.Guild && n.Data == guildID);
            Assert.StartsWith($"{name} {founder.User.Name} ", named?.Strings[0]);

            // Only the leader can't leave; members are expelled from the member list
            await founder.Game.SyndicateAsync(SyndicateMessage.Leave, 0);
            var refused = await this.Servers.WaitForEventAsync("game", "guild.rejected", "name", founder.User.Name);
            Assert.Equal("leader", refused.GetProperty("reason").GetString());
            await founder.Game.NameAsync(NameMessage.GuildKick, member.User.Name);
            Assert.NotNull(await member.Game.WaitForGuildInfoAsync(i => i.GuildId == 0));
            var left = await this.Servers.WaitForEventAsync("game", "guild.left", "name", member.User.Name);
            Assert.Equal("kicked", left.GetProperty("reason").GetString());

            // The guild outlives the leader's session
            await this.LogOutAsync(founder);
            await using var again = await this.Servers.PlayAsync(founderEmail, Password);
            Assert.Equal((guildID, Leader, 500_100ul), (again.Game.Guild?.GuildId, again.Game.Guild?.Rank, again.Game.Guild?.Fund));
        }

        [Fact]
        public async Task LeadersAllyTheirGuildsAndOthersCanBeEnemies()
        {
            await using var first = (await this.FounderAsync(402, 396)).Player;
            await using var second = (await this.FounderAsync(403, 396)).Player;
            var firstName = NewGuildName();
            var secondName = NewGuildName();
            var firstID = await CreateAsync(first, firstName);
            var secondID = await CreateAsync(second, secondName);

            await first.Game.SyndicateAsync(SyndicateMessage.Ally, 0, secondName);
            var asked = await second.Game.WaitForSyndicateAsync(s => s.Action == SyndicateMessage.AllyRequest);
            Assert.Equal((firstID, firstName), (asked?.Data, asked?.Text));
            await second.Game.SyndicateAsync(SyndicateMessage.AllyRequest, firstID);
            Assert.NotNull(await first.Game.WaitForNameAsync(n => n.Action == NameMessage.SetAlly && n.Data == secondID));
            Assert.NotNull(await second.Game.WaitForNameAsync(n => n.Action == NameMessage.SetAlly && n.Data == firstID));

            // An ally can't be an enemy until the alliance ends
            await first.Game.SyndicateAsync(SyndicateMessage.Enemy, 0, secondName);
            var refused = await this.Servers.WaitForEventAsync("game", "guild.rejected", "name", first.User.Name);
            Assert.Equal("ally", refused.GetProperty("reason").GetString());
            await first.Game.SyndicateAsync(SyndicateMessage.Unally, 0, secondName);
            Assert.NotNull(await second.Game.WaitForSyndicateAsync(s => s.Action == SyndicateMessage.Unally && s.Data == firstID));
            await first.Game.SyndicateAsync(SyndicateMessage.Enemy, 0, secondName);
            Assert.NotNull(await first.Game.WaitForNameAsync(n => n.Action == NameMessage.SetEnemy && n.Data == secondID));
        }

        [Fact]
        public async Task DonationRankingReturnsMembersInOrderBySelectedColumn()
        {
            const uint Jewels = 29;
            await using var leader = (await this.FounderAsync(406, 396)).Player;
            await using var member = (await this.PlayingAtAsync(1002, 407, 396, GameMaster)).Player;
            var guildID = await CreateAsync(leader, NewGuildName());
            await RecruitAsync(leader, member, guildID);

            await CommandAsync(leader, "/jewel 100");
            Assert.NotNull(await leader.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Jewels, out var v) && v == 100));
            await CommandAsync(member, "/silver 5000");
            Assert.NotNull(await member.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 6_000));
            await CommandAsync(member, "/jewel 25");
            Assert.NotNull(await member.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Jewels, out var v) && v == 25));

            // Member donates more silver; leader donates more CPs
            await leader.Game.SyndicateAsync(SyndicateMessage.DonateSilver, 300);
            Assert.NotNull(await leader.Game.WaitForGuildInfoAsync(i => i.Donation == 300));
            await leader.Game.SyndicateAsync(SyndicateMessage.DonateConquerPoints, 80);
            Assert.NotNull(await leader.Game.WaitForGuildInfoAsync(i => i.ConquerPointsFund == 80));

            await member.Game.SyndicateAsync(SyndicateMessage.DonateSilver, 900);
            Assert.NotNull(await member.Game.WaitForGuildInfoAsync(i => i.Donation == 900));
            await member.Game.SyndicateAsync(SyndicateMessage.DonateConquerPoints, 20);
            Assert.NotNull(await member.Game.WaitForGuildInfoAsync(i => i.ConquerPointsFund == 100));

            var bySilver = await leader.Game.QueryGuildDonationRankingAsync(0);
            Assert.NotNull(bySilver);
            Assert.Equal((byte)0, bySilver.RankType);
            Assert.Equal((ushort)2, bySilver.MaxRankSize);
            Assert.Equal(new[] { (member.User.Name, 0u, 900u, 20u, 920u), (leader.User.Name, 1u, 300u, 80u, 380u) },
                bySilver.Entries.Select(e => (e.Name, e.Position, e.Silver, e.ConquerPoints, e.Total)));

            var byCps = await leader.Game.QueryGuildDonationRankingAsync(1);
            Assert.NotNull(byCps);
            Assert.Equal((byte)1, byCps.RankType);
            Assert.Equal(new[] { (leader.User.Name, 0u, 80u), (member.User.Name, 1u, 20u) },
                byCps.Entries.Select(e => (e.Name, e.Position, e.ConquerPoints)));

            var byTotal = await member.Game.QueryGuildDonationRankingAsync(9);
            Assert.NotNull(byTotal);
            Assert.Equal(new[] { (member.User.Name, 0u, 920u), (leader.User.Name, 1u, 380u) },
                byTotal.Entries.Select(e => (e.Name, e.Position, e.Total)));

            // Unimplemented column (arsenal = 4) still answers with zero for that column
            var byArsenal = await leader.Game.QueryGuildDonationRankingAsync(4);
            Assert.NotNull(byArsenal);
            Assert.Equal((ushort)2, byArsenal.MaxRankSize);
            Assert.All(byArsenal.Entries, e => Assert.Equal(0u, e.Arsenal));
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    // A guild leader puts silver from the guild's fund into the Capture the Flag reward pool, earns
    // exploits by taking a flag, and gets the whole pool when the event ends
    public class GuildRewardTests : IntegrationTest
    {
        private const uint GuildDirector = 10003, GuildController = 380;
        private const uint Arena = ServerFixture.CaptureTheFlagArena;

        public GuildRewardTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task TheLeadersPoolIsPaidByExploitsWhenCaptureTheFlagEnds()
        {
            var (leader, _) = await this.PlayingAtAsync(1002, 398, 396, GameMaster, level: 90, strength: 2500);
            await using var _l = leader;
            await CommandAsync(leader, "/silver 1000000");
            var guildName = "R" + Guid.NewGuid().ToString("N")[..8];
            await leader.Game.TalkToNpcAsync(GuildDirector);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(1);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(10, guildName);
            var guild = await leader.Game.WaitForGuildInfoAsync(i => i.GuildId != 0);
            Assert.NotNull(guild);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            var fund = Convert.ToUInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT Fund FROM syndicate WHERE SyndicateID = @id", ("@id", guild.GuildId)));

            // Below 100 is refused; the guild has no CPs anyway
            await leader.Game.AwardAsync(AwardMessage.AddConquerPoints, AwardMessage.CaptureTheFlag, conquerPoints: 50);
            await this.Servers.WaitForEventAsync("game", "guildreward.rejected", "reason", "minimum");

            // 5000 silver leave the fund for the pool, saved at once
            await leader.Game.AwardAsync(AwardMessage.AddSilver, AwardMessage.CaptureTheFlag, silver: 5000);
            var set = await leader.Game.WaitForAwardAsync(m => m.Action == AwardMessage.AddSilver);
            Assert.Equal((0u, 5000ul), (set?.ConquerPoints, set?.Silver));
            Assert.Equal(fund - 5000, Convert.ToUInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT Fund FROM syndicate WHERE SyndicateID = @id", ("@id", guild.GuildId))));
            Assert.Equal(5000ul, Convert.ToUInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT Silver FROM guild_reward_pool WHERE SyndicateID = @id AND Event = 1", ("@id", guild.GuildId))));

            // Adding to it needs no minimum
            await leader.Game.AwardAsync(AwardMessage.AddSilver, AwardMessage.CaptureTheFlag, silver: 1);
            Assert.NotNull(await leader.Game.WaitForAwardAsync(m => m.Action == AwardMessage.AddSilver && m.Silver == 5001));

            // Taking a flag is 3 exploits
            await CommandAsync(leader, "/tp 1002 350 337");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await CommandAsync(leader, "/ctf start");
            await this.Servers.WaitForEventAsync("game", "ctf.started", _ => true);
            await leader.Game.TalkToNpcAsync(GuildController);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(4);
            Assert.Equal(Arena, (await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id))?.Command);
            var flags = await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.FlagPositions && m.Positions.Count > 0);
            Assert.NotNull(flags);
            var (fx, fy) = flags.Positions[0];
            await CommandAsync(leader, $"/tp {Arena} {(fx > 0 ? fx - 1 : fx + 1)} {fy}");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            var flag = await leader.Game.WaitForGoalAsync(g => g.Role == 47 && g.X == fx && g.Y == fy);
            Assert.NotNull(flag);
            await leader.Game.WarFlagAsync(WarFlagMessage.PickedUp, flag.Id);
            Assert.NotNull(await leader.Game.WaitForAwardAsync(m => m.Action == AwardMessage.OwnExploits && m.ConquerPoints == 3));
            await leader.Game.AwardAsync(AwardMessage.ExploitRanking, AwardMessage.CaptureTheFlag, page: 1);
            var ranking = await leader.Game.WaitForAwardAsync(m => m.Action == AwardMessage.ExploitRanking);
            Assert.Equal((leader.User.Name, 3u), Assert.Single(ranking.Exploits));

            // The end splits the pool: the only earner gets all of it
            await CommandAsync(leader, "/ctf end");
            var paid = await this.Servers.WaitForEventAsync("game", "guildreward.paid", "name", leader.User.Name);
            Assert.Equal(5001ul, paid.GetProperty("silver").GetUInt64());
            Assert.Null(await this.Servers.QueryGameScalarAsync(
                "SELECT Silver FROM guild_reward_pool WHERE SyndicateID = @id AND Event = 1", ("@id", guild.GuildId)));
            await leader.Game.AwardAsync(AwardMessage.MemberShares, AwardMessage.CaptureTheFlag, page: 1);
            var shares = await leader.Game.WaitForAwardAsync(m => m.Action == AwardMessage.MemberShares);
            var share = Assert.Single(shares.Shares);
            Assert.Equal((3u, 0u, 5001ul, leader.User.Id), (share.Exploits, share.ConquerPoints, share.Silver, share.CharacterId));
            Assert.Equal((0u, 0ul), (shares.ConquerPoints, shares.Silver));
        }
    }
}

namespace Fetih.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    // Capture the Flag started by a game master: a guild member enters at the Guild Controller,
    // occupies a flag base, carries a flag into it, and the guild's prize goes to its fund
    public class CaptureTheFlagTests : IntegrationTest
    {
        private const uint GuildDirector = 10003, GuildController = 380;
        private const uint Arena = ServerFixture.CaptureTheFlagArena;
        private const uint FirstBase = 700001;
        private const ushort BaseX = 328, BaseY = 449;

        public CaptureTheFlagTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task GuildOccupiesABasePlantsAFlagAndWinsThePrize()
        {
            var (leader, _) = await this.PlayingAtAsync(1002, 398, 396, GameMaster, level: 90, strength: 2500);
            await using var _l = leader;
            await CommandAsync(leader, "/silver 1000000");
            var guildName = "F" + Guid.NewGuid().ToString("N")[..8];
            await leader.Game.TalkToNpcAsync(GuildDirector);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(1);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(10, guildName);
            var guild = await leader.Game.WaitForGuildInfoAsync(i => i.GuildId != 0);
            Assert.NotNull(guild);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            var fundBefore = Convert.ToUInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT Fund FROM syndicate WHERE SyndicateID = @id", ("@id", guild.GuildId)));

            await CommandAsync(leader, "/tp 1002 350 337");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));

            // The GM starts it and the Controller lets guild members in, to the safe zone
            await CommandAsync(leader, "/ctf start");
            await this.Servers.WaitForEventAsync("game", "ctf.started", _ => true);
            await leader.Game.TalkToNpcAsync(GuildController);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(4);
            var entered = await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id);
            Assert.Equal(Arena, entered?.Command);
            Assert.InRange(entered.X, 439, 524);
            Assert.InRange(entered.Y, 325, 410);
            Assert.NotNull(await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.Enter));
            var flags = await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.FlagPositions && m.Positions.Count > 0);
            Assert.NotNull(flags);
            var (fx, fy) = flags.Positions[0];

            // Hitting the base occupies it for the guild; the ranking then lists the base for the radar
            await CommandAsync(leader, $"/tp {Arena} {BaseX} {BaseY - 1}");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            Assert.NotNull(await leader.Game.WaitForGoalAsync(FirstBase));
            await leader.Game.AttackAsync(leader.User.Id, FirstBase, BaseX, BaseY);
            await this.Servers.WaitForEventAsync("game", "ctf.occupied", "guild", guildName);
            var rank = await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.ScoreRank && m.GuildId == guild.GuildId);
            Assert.NotNull(rank);

            // Touching a flag carries it
            await CommandAsync(leader, $"/tp {Arena} {(fx > 0 ? fx - 1 : fx + 1)} {fy}");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            var flag = await leader.Game.WaitForGoalAsync(g => g.Role == 47 && g.X == fx && g.Y == fy);
            Assert.NotNull(flag);
            await leader.Game.WarFlagAsync(WarFlagMessage.PickedUp, flag.Id);
            Assert.NotNull(await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.PickedUp && m.Data == leader.User.Id));
            Assert.Equal(60u, (await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.Countdown))?.Data);

            // Reaching the guild's base plants it: 3 points for the flag, 15 for planting
            await CommandAsync(leader, $"/tp {Arena} {BaseX} {BaseY}");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.WarFlagAsync(WarFlagMessage.Planted);
            Assert.NotNull(await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.Planted && m.Data == leader.User.Id));
            var planted = await this.Servers.WaitForEventAsync("game", "ctf.planted", "name", leader.User.Name);
            Assert.Equal(15, planted.GetProperty("points").GetInt32());
            var scored = await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.ScoreRank &&
                m.Ranks.Any(r => r.Name == guildName && r.Score == 18), TimeSpan.FromSeconds(15));
            Assert.NotNull(scored);

            // The end sends everyone back and pays the first place's prize into the guild fund
            await CommandAsync(leader, "/ctf end");
            await this.Servers.WaitForEventAsync("game", "ctf.rewarded", "guild", guildName);
            Assert.NotNull(await leader.Game.WaitForWarFlagAsync(m => m.Action == WarFlagMessage.GameOver));
            var home = await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id);
            Assert.Equal(1002u, home?.Command);
            Assert.Equal(fundBefore + 120_000_000ul, Convert.ToUInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT Fund FROM syndicate WHERE SyndicateID = @id", ("@id", guild.GuildId))));
        }
    }
}

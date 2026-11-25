namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // The Guild Contest and the Guild PK Tournament's final, run by a game master: two guilds
    // score in the contest, qualify, and meet in the final, where one seizes the flag
    public class GuildPkTests : IntegrationTest
    {
        private const uint GuildDirector = 10003, TwinCity = 1002;
        private const uint ContestEmissary = 7881, PkEmissary = 7882, FirstAwarder = 7883, SecondAwarder = 7884;
        private const uint Flag = 7891;
        private const uint ConquerPoints = 29;

        public GuildPkTests(ServerFixture servers) : base(servers) { }

        // At the Guild Director, then near the Emissaries
        private async Task<(Player Player, uint GuildId, string GuildName)> CreateGuildAsync(int x)
        {
            var (player, _) = await this.PlayingAtAsync(TwinCity, x, 396, GameMaster, level: 90, strength: 5000);
            await CommandAsync(player, "/silver 1000000");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_001_000));
            var name = "P" + Guid.NewGuid().ToString("N")[..8];
            await player.Game.TalkToNpcAsync(GuildDirector);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(10, name);
            var info = await player.Game.WaitForGuildInfoAsync(i => i.GuildId != 0);
            Assert.NotNull(info);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            // Next to the Emissaries and Awarders
            await CommandAsync(player, $"/tp {TwinCity} {x + 38} 254");
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            return (player, info.GuildId, name);
        }

        private static async Task TalkAsync(Player player, uint npc, byte option)
        {
            await player.Game.TalkToNpcAsync(npc);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(option);
        }

        private async Task KillContestPheasantAsync(Player player, (ushort X, ushort Y) at)
        {
            await TalkAsync(player, ContestEmissary, 1);
            Assert.Equal(ServerFixture.GuildContestMap, (await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id))?.Command);
            await CommandAsync(player, $"/tp {ServerFixture.GuildContestMap} {at.X - 1} {at.Y}");
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            var pheasant = await player.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(pheasant);
            await player.Game.AttackAsync(player.User.Id, pheasant.Id, at.X, at.Y);
            Assert.NotNull(await player.Game.WaitForInteractAsync(i => i.Action == InteractMessage.Kill && i.Target == pheasant.Id));
        }

        private static async Task<ushort> EnterMatchAsync(Player player)
        {
            await TalkAsync(player, PkEmissary, 1);
            var arrived = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal(ServerFixture.GuildPkMap, arrived?.Command);
            return arrived.X;
        }

        [Fact]
        public async Task TwoGuildsQualifyInTheContestAndTheFlagDecidesTheFinal()
        {
            var (a, guildA, nameA) = await this.CreateGuildAsync(398);
            await using var _a = a;
            var (b, guildB, nameB) = await this.CreateGuildAsync(400);
            await using var _b = b;

            // The contest: a pheasant each, so both score a point; the older guild ranks first
            await CommandAsync(a, "/guildpk contest start");
            await this.Servers.WaitForEventAsync("game", "guildpk.contest_started", _ => true);
            await this.KillContestPheasantAsync(a, ServerFixture.ContestPheasantA);
            await this.KillContestPheasantAsync(b, ServerFixture.ContestPheasantB);
            await CommandAsync(a, "/guildpk contest end");
            await this.Servers.WaitForEventAsync("game", "guildpk.contest_ended", _ => true);
            foreach (var player in new[] { a, b })
                Assert.Equal(TwinCity, (await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id))?.Command);
            Assert.Equal(1, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Seed FROM guild_pk_entry WHERE SyndicateID = @id", ("@id", guildA))));
            Assert.Equal(2, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Seed FROM guild_pk_entry WHERE SyndicateID = @id", ("@id", guildB))));
            Assert.Equal(1u, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Exploits FROM guild_pk_exploit WHERE CharacterID = @id", ("@id", a.User.Id))));

            // B's leader is sent to the Awarder of second place; A's claims first place's CPs
            await TalkAsync(b, FirstAwarder, 3);
            Assert.Contains("placed 2", (await b.Game.ReadDialogAsync())?[0].Text);
            await TalkAsync(a, FirstAwarder, 3);
            Assert.NotNull(await a.Game.WaitForAttribAsync(x => x.Values.TryGetValue(ConquerPoints, out var cps) && cps >= 1000));
            await this.Servers.WaitForEventAsync("game", "guildpk.prize_claimed", "name", a.User.Name);

            // The final: seed 1 (Group A) meets seed 2 (Group B), each from its side of the flag
            await CommandAsync(a, "/guildpk final start");
            await this.Servers.WaitForEventAsync("game", "guildpk.matches_started", _ => true);
            ushort ax = await EnterMatchAsync(a);
            ushort bx = await EnterMatchAsync(b);
            Assert.True(ax < 20 && bx > 20);

            // A seizes the flag in the middle and holds it a few seconds
            var flag = await a.Game.WaitForGoalAsync(Flag);
            Assert.Equal((1000u, (ushort)10), (flag?.MaxLife, flag.Role));
            await a.Game.JumpAsync(a.User.Id, ax, 20, 19, 20);
            await a.Game.AttackAsync(a.User.Id, Flag, 20, 20);
            await this.Servers.WaitForEventAsync("game", "guildpk.flag_captured", "guild", nameA);
            Assert.Equal(nameA, (await a.Game.WaitForGoalAsync(Flag))?.Name);

            // Defeating an enemy is an Exploit and no crime
            await b.Game.JumpAsync(b.User.Id, bx, 20, 21, 20);
            await a.Game.AttackAsync(a.User.Id, b.User.Id, 21, 20);
            Assert.NotNull(await b.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == b.User.Id, TimeSpan.FromSeconds(15)));
            await Task.Delay(TimeSpan.FromSeconds(2));

            await CommandAsync(a, "/guildpk final end");
            var ended = await this.Servers.WaitForEventAsync("game", "guildpk.match_ended", "winner", nameA);
            Assert.True(ended.GetProperty("scoreA").GetInt64() >= 2);
            Assert.Equal(nameB, ended.GetProperty("guildB").GetString());
            foreach (var player in new[] { a, b })
                Assert.Equal(TwinCity, (await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id))?.Command);
            Assert.Equal(1, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT FinalRank FROM guild_pk_entry WHERE SyndicateID = @id", ("@id", guildA))));
            Assert.Equal(2, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT `Rank` FROM guild_pk_prize WHERE SyndicateID = @id AND Kind = 1", ("@id", guildB))));

            Assert.Equal(2u, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Exploits FROM guild_pk_exploit WHERE CharacterID = @id", ("@id", a.User.Id))));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT KillPoints FROM `character` WHERE CharacterID = @id", ("@id", a.User.Id))));

            // The champion's prize
            int mark = this.Servers.Mark();
            await TalkAsync(a, FirstAwarder, 4);
            Assert.NotNull(await a.Game.WaitForAttribAsync(x => x.Values.TryGetValue(ConquerPoints, out var cps) && cps >= 6000));
            await this.Servers.WaitForEventAsync("game", "guildpk.prize_claimed", "name", a.User.Name, mark);
            Assert.True(Convert.ToBoolean(await this.Servers.QueryGameScalarAsync(
                "SELECT Claimed FROM guild_pk_prize WHERE SyndicateID = @id AND Kind = 1", ("@id", guildA))));
        }
    }
}

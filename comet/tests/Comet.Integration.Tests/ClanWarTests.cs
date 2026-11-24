namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.FamilyOccupyMessage;

    public class ClanWarTests : IntegrationTest
    {
        private const uint ClanManager = 5533;
        private const uint TwinCityBanner = 101887;
        private const uint Silver = 4;

        public ClanWarTests(ServerFixture servers) : base(servers) { }

        // A level 50 game master with a new clan, standing by Twin City's clan banner
        private async Task<(Player Player, uint ClanId, string ClanName)> LeaderAsync(int x, ushort strength = 0)
        {
            var (player, _) = await this.PlayingAtAsync(1002, 404, 393, GameMaster, level: 50, strength: strength);
            await CommandAsync(player, "/silver 1000000");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_001_000));
            var name = "K" + Guid.NewGuid().ToString("N")[..8];
            await player.Game.TalkToNpcAsync(ClanManager);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(10, name);
            var info = await player.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Query);
            Assert.Equal(name, info?.Strings[1]);
            await CommandAsync(player, $"/tp 1002 {x} 352");
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id));
            return (player, info.Data, name);
        }

        [Fact]
        public async Task ClansSignUpFightAndTheWinnerOccupiesTheMapAcrossAReload()
        {
            var (first, firstId, firstName) = await this.LeaderAsync(470, strength: 5000);
            await using var _ = first;
            var (second, secondId, secondName) = await this.LeaderAsync(472);
            await using var __ = second;

            // The banner's dialog, before anyone holds the map
            await first.Game.FamilyOccupyAsync(Query, TwinCityBanner);
            var dialog = await first.Game.WaitForFamilyOccupyAsync(d => d.Action == Query);
            Assert.Equal((TwinCityBanner, 5u, "", "Twin City", true, 0u, 500_000ul),
                (dialog?.NpcId, dialog.Role, dialog.Occupier, dialog.MapName, dialog.CanApply, dialog.Days, dialog.Fee));
            Assert.Equal((722458u, 722457u), (dialog.DailyPrize, dialog.WeeklyPrize));

            // Both leaders sign up and pay the fee
            await first.Game.FamilyOccupyAsync(Apply, TwinCityBanner);
            var signed = await first.Game.WaitForFamilyOccupyAsync(d => d.Action == Query && d.Role == 3);
            Assert.True(signed?.Challenged);
            Assert.NotNull(await first.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_000));
            var line = await first.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.Query && f.Strings.Count > 5);
            Assert.Equal(($"{TwinCityBanner} 0 0 0 0 0 0", "", "Twin City"), (line?.Strings[3], line.Strings[4], line.Strings[5]));
            await second.Game.FamilyOccupyAsync(Apply, TwinCityBanner);
            Assert.NotNull(await second.Game.WaitForFamilyOccupyAsync(d => d.Action == Query && d.Role == 3));
            Assert.Equal(TwinCityBanner, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT NpcID FROM family_war_signup WHERE FamilyID = @id", ("@id", secondId))));

            // The war starts: both clans are invited and enter their own battlefield
            await CommandAsync(first, "/clanwar start");
            var invite = await first.Game.WaitForFamilyOccupyAsync(d => d.Action == Invite);
            Assert.Equal((TwinCityBanner, "Twin City"), (invite?.NpcId, invite.MapName));
            Assert.NotNull(await second.Game.WaitForFamilyOccupyAsync(d => d.Action == Invite));
            await first.Game.FamilyOccupyAsync(Join, TwinCityBanner);
            var entered = await first.Game.WaitForActionAsync(ActionMessage.Teleport, first.User.Id);
            Assert.Equal((ServerFixture.ClanWarField, (ushort)100, (ushort)120), (entered?.Command, entered.X, entered.Y));
            await second.Game.FamilyOccupyAsync(Join, TwinCityBanner);
            Assert.NotNull(await second.Game.WaitForActionAsync(ActionMessage.Teleport, second.User.Id));
            Assert.NotNull(await first.Game.WaitForSpawnAsync(second.User.Id));

            // Fighting there is no crime, whatever the PK mode
            await first.Game.AttackAsync(first.User.Id, second.User.Id, 100, 120);
            var kill = await this.Servers.WaitForEventAsync("game", "clanwar.kill", "name", first.User.Name);
            Assert.Equal(firstId, kill.GetProperty("clanId").GetUInt32());
            Assert.Null(await first.Game.WaitForAttribAsync(a =>
                a.Id == first.User.Id && a.Values.TryGetValue(UserAttribMessage.PkPoints, out var p) && p > 0, Already));

            int mark = this.Servers.Mark();
            await CommandAsync(first, "/clanwar end");
            var ended = await this.Servers.WaitForEventAsync("game", "clanwar.ended", "winnerName", firstName, mark);
            Assert.Equal(firstId, ended.GetProperty("winnerId").GetUInt32());
            Assert.NotNull(await first.Game.WaitForTalkAsync(t =>
                t.Channel == TalkChannels.Center && t.Text.Contains($"{firstName} has won the Clan War of Twin City")));
            var back = await first.Game.WaitForActionAsync(ActionMessage.Teleport, first.User.Id);
            Assert.Equal((1002u, (ushort)470, (ushort)352), (back?.Command, back.X, back.Y));

            Assert.Equal(firstId, Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT FamilyID FROM family_occupy WHERE NpcID = @npc", ("@npc", TwinCityBanner))));
            Assert.Equal(0L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM family_war_signup WHERE NpcID = @npc", ("@npc", TwinCityBanner))));

            // Read back from the database, as after a restart
            await CommandAsync(first, "/clanwar reload");
            await this.Servers.WaitForEventAsync("game", "clanwar.loaded", _ => true, after: mark);
            await first.Game.FamilyOccupyAsync(Query, TwinCityBanner);
            var held = await first.Game.WaitForFamilyOccupyAsync(d => d.Action == Query && d.Role == 1);
            Assert.Equal((firstName, 1u, true, false), (held?.Occupier, held.Days, held.RevenueUnclaimed, held.CanApply));
            await first.Game.FamilyAsync(FamilyMessage.QueryOccupy, 0);
            var occupation = await first.Game.WaitForFamilyAsync(f => f.Action == FamilyMessage.QueryOccupy);
            Assert.Equal("0 1 722458 722457 0 0 0", occupation?.Text);

            // The leader claims the day's revenue once
            await first.Game.FamilyOccupyAsync(ClaimRevenue, TwinCityBanner);
            var claimed = await first.Game.WaitForFamilyOccupyAsync(d => d.Action == RevenueClaimed);
            Assert.Equal(first.User.Id, claimed?.PlayerId);
            Assert.NotNull(await first.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.NormalTwinCityClanPack.Id));
            await first.Game.FamilyOccupyAsync(ClaimRevenue, TwinCityBanner);
            Assert.NotNull(await first.Game.WaitForFamilyOccupyAsync(d => d.Action == OnceADay));

            // The other clan sees who holds the map
            await second.Game.FamilyOccupyAsync(Query, TwinCityBanner);
            var seen = await second.Game.WaitForFamilyOccupyAsync(d => d.Action == Query && d.Occupier == firstName);
            Assert.Equal(5u, seen?.Role);
        }
    }
}

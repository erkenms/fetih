namespace Fetih.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    public class GuildWarTests : IntegrationTest
    {
        private const uint GuildDirector = 10003;
        private const uint GuildController = 380;
        private const uint Pole = 6700, LeftGate = 6701, RightGate = 6702;
        private const uint GuildGateKeeper = 7000, BeastGuard = 101633, Conductor2 = 101615, FlameStone2 = 4454;

        public GuildWarTests(ServerFixture servers) : base(servers) { }

        private static string NewGuildName() => "W" + Guid.NewGuid().ToString("N")[..8];

        private async Task<(Player Player, uint GuildId, string GuildName)> CreateGuildAsync(int x, int y, ushort strength = 2500)
        {
            var (player, _) = await this.PlayingAtAsync(1002, x, y, GameMaster, level: 90, strength: strength);
            await CommandAsync(player, "/silver 1000000");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Silver, out var v) && v == 1_001_000));
            var name = NewGuildName();
            await player.Game.TalkToNpcAsync(GuildDirector);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(10, name);
            var info = await player.Game.WaitForGuildInfoAsync(i => i.GuildId != 0);
            Assert.NotNull(info);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            return (player, info.GuildId, name);
        }

        [Fact]
        public async Task AttackingGateOpensItAttackingPoleCapturesItAndWinnerPersistsAndClaimsPrize()
        {
            var (leader, guildId, guildName) = await this.CreateGuildAsync(398, 396, strength: 2500);
            await using var _ = leader;

            // Enter map 1038 via GuildController
            await CommandAsync(leader, "/tp 1002 350 337");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.TalkToNpcAsync(GuildController);
            Assert.Contains("Guild War", (await leader.Game.ReadDialogAsync())?[0].Text);
            await leader.Game.AnswerNpcAsync(1);
            var entered = await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id);
            Assert.Equal((ServerFixture.GuildWar, (ushort)350, (ushort)339), (entered?.Command, entered?.X, entered?.Y));

            // Start the war
            await CommandAsync(leader, "/guildwar start");
            Assert.NotNull(await leader.Game.WaitForTalkAsync(t =>
                t.Channel == TalkChannels.Center && t.Text.Contains("Guild War has begun")));

            // Move next to LeftGate (163, 211), see closed gate (241), break it open (251)
            await CommandAsync(leader, "/tp 1038 164 211");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            var gate = await leader.Game.WaitForGoalAsync(LeftGate);
            Assert.Equal((1000u, 1000u, (ushort)241, (ushort)26), (gate?.MaxLife, gate.Life, gate.Lookface, gate.Role));

            await leader.Game.AttackAsync(leader.User.Id, LeftGate, 163, 211);
            var opened = await leader.Game.WaitForGoalAsync(LeftGate);
            Assert.Equal((0u, 0u, (ushort)251), (opened?.MaxLife, opened.Life, opened.Lookface));

            // Move next to Pole (84, 99), attack it until captured
            await CommandAsync(leader, "/tp 1038 85 99");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            var pole = await leader.Game.WaitForGoalAsync(Pole);
            Assert.Equal((2000u, 2000u, (ushort)1135, (ushort)10), (pole?.MaxLife, pole.Life, pole.Lookface, pole.Role));

            int mark = this.Servers.Mark();
            await leader.Game.AttackAsync(leader.User.Id, Pole, 84, 99);
            var capturedEvent = await this.Servers.WaitForEventAsync("game", "guildwar.captured", "guildName", guildName, mark);
            Assert.Equal(guildId, capturedEvent.GetProperty("guildId").GetUInt32());
            Assert.NotNull(await leader.Game.WaitForTalkAsync(t =>
                t.Channel == TalkChannels.Center && t.Text.Contains($"{guildName} has captured")));
            var poleAfter = await leader.Game.WaitForGoalAsync(Pole);
            Assert.Equal((2000u, 2000u, guildName), (poleAfter?.MaxLife, poleAfter.Life, poleAfter.Name));

            // Holding guild cannot attack its own pole
            int rejectMark = this.Servers.Mark();
            await leader.Game.AttackAsync(leader.User.Id, Pole, 84, 99);
            var rejected = await this.Servers.WaitForEventAsync("game", "combat.rejected", "name", leader.User.Name, rejectMark);
            Assert.Equal("own_pole", rejected.GetProperty("reason").GetString());

            // Holding guild can pass through and toggle LeftGate
            await CommandAsync(leader, "/tp 1038 164 211");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            var resetGate = await leader.Game.WaitForGoalAsync(LeftGate);
            Assert.Equal((ushort)241, resetGate?.Lookface);

            await leader.Game.TalkToNpcAsync(LeftGate);
            Assert.Contains("Victorious", (await leader.Game.ReadDialogAsync())?[0].Text);
            await leader.Game.AnswerNpcAsync(2);
            Assert.Contains("open", (await leader.Game.ReadDialogAsync())?[0].Text);
            var toggledOpen = await leader.Game.WaitForGoalAsync(LeftGate);
            Assert.Equal((ushort)251, toggledOpen?.Lookface);

            await leader.Game.TalkToNpcAsync(LeftGate);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(1);
            var inside = await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id);
            Assert.Equal((ServerFixture.GuildWar, (ushort)162, (ushort)199), (inside?.Command, inside?.X, inside?.Y));

            // Map 1038 NPCs have real dialogs
            await CommandAsync(leader, "/tp 1038 56 58");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.TalkToNpcAsync(BeastGuard);
            Assert.Contains(guildName, (await leader.Game.ReadDialogAsync())?[0].Text);

            await CommandAsync(leader, "/tp 1038 317 272");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.TalkToNpcAsync(FlameStone2);
            Assert.Contains("Flame Stone", (await leader.Game.ReadDialogAsync())?[0].Text);

            // End the war and verify DB persistence
            int endMark = this.Servers.Mark();
            await CommandAsync(leader, "/guildwar end");
            var ended = await this.Servers.WaitForEventAsync("game", "guildwar.ended", "winnerName", guildName, endMark);
            Assert.Equal(guildId, ended.GetProperty("winnerId").GetUInt32());
            Assert.NotNull(await leader.Game.WaitForTalkAsync(t =>
                t.Channel == TalkChannels.Center && t.Text.Contains($"{guildName} is victorious")));

            var savedWinner = Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT WinnerGuildID FROM guild_war WHERE ID = 1"));
            var savedClaimed = Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT RewardClaimed FROM guild_war WHERE ID = 1"));
            Assert.Equal(guildId, savedWinner);
            Assert.Equal(0, savedClaimed);

            // Claim the 3,000 CPs prize at GuildController
            await CommandAsync(leader, "/tp 1002 350 337");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.TalkToNpcAsync(GuildController);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(2);
            Assert.Contains("3,000 CPs", (await leader.Game.ReadDialogAsync())?[0].Text);
            Assert.NotNull(await leader.Game.WaitForAttribAsync(a => a.Values.TryGetValue(Jewels, out var cp) && cp == 3000));

            savedClaimed = Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT RewardClaimed FROM guild_war WHERE ID = 1"));
            Assert.Equal(1, savedClaimed);

            // Second claim is refused
            await leader.Game.TalkToNpcAsync(GuildController);
            Assert.NotNull(await leader.Game.ReadDialogAsync());
            await leader.Game.AnswerNpcAsync(2);
            Assert.Contains("already been claimed", (await leader.Game.ReadDialogAsync())?[0].Text);

            // GuildGateKeeper & GuildConductor2 teleport out
            await CommandAsync(leader, "/tp 1038 354 344");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.TalkToNpcAsync(GuildGateKeeper);
            Assert.Contains("leave", (await leader.Game.ReadDialogAsync())?[0].Text);
            await leader.Game.AnswerNpcAsync(1);
            var left = await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id);
            Assert.Equal((1002u, (ushort)430, (ushort)380), (left?.Command, left?.X, left?.Y));

            await CommandAsync(leader, "/tp 1038 339 339");
            Assert.NotNull(await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id));
            await leader.Game.TalkToNpcAsync(Conductor2);
            Assert.Contains("Desert City", (await leader.Game.ReadDialogAsync())?[0].Text);
            await leader.Game.AnswerNpcAsync(1);
            var toDesert = await leader.Game.WaitForActionAsync(ActionMessage.Teleport, leader.User.Id);
            Assert.Equal((ServerFixture.DesertCity, (ushort)492, (ushort)669), (toDesert?.Command, toDesert?.X, toDesert?.Y));
        }

        [Fact]
        public async Task NonHoldingGuildIsRefusedAtGateAndHigherDamageGuildCapturesPole()
        {
            var (first, _, firstName) = await this.CreateGuildAsync(398, 396, strength: 1200);
            var (second, secondId, secondName) = await this.CreateGuildAsync(399, 396, strength: 2500);
            await using var _ = first;
            await using var __ = second;

            await CommandAsync(first, "/guildwar start");
            Assert.NotNull(await first.Game.WaitForTalkAsync(t =>
                t.Channel == TalkChannels.Center && t.Text.Contains("Guild War has begun")));

            // First guild deals ~1200 damage to the 2000-HP pole, second guild finishes it with higher damage
            await CommandAsync(first, "/tp 1038 85 99");
            Assert.NotNull(await first.Game.WaitForActionAsync(ActionMessage.Teleport, first.User.Id));
            Assert.NotNull(await first.Game.WaitForGoalAsync(Pole));
            await CommandAsync(second, "/tp 1038 83 99");
            Assert.NotNull(await second.Game.WaitForActionAsync(ActionMessage.Teleport, second.User.Id));
            Assert.NotNull(await second.Game.WaitForGoalAsync(Pole));

            int mark = this.Servers.Mark();
            await second.Game.AttackAsync(second.User.Id, Pole, 84, 99);
            var captured = await this.Servers.WaitForEventAsync("game", "guildwar.captured", "guildName", secondName, mark);
            Assert.Equal(secondId, captured.GetProperty("guildId").GetUInt32());

            // Non-holding guild is refused at LeftGate
            await CommandAsync(first, "/tp 1038 164 211");
            Assert.NotNull(await first.Game.WaitForActionAsync(ActionMessage.Teleport, first.User.Id));
            await first.Game.TalkToNpcAsync(LeftGate);
            Assert.Contains("Only members of the guild holding the pole", (await first.Game.ReadDialogAsync())?[0].Text);

            await CommandAsync(first, "/guildwar end");
            Assert.NotNull(await first.Game.WaitForTalkAsync(t =>
                t.Channel == TalkChannels.Center && t.Text.Contains($"{secondName} is victorious")));
        }
    }
}

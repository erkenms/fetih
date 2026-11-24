namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class DetainedItemTests : IntegrationTest
    {
        private const uint MarketMap = 1036;
        private const uint PrizeOfficerMarket = 4450;

        public DetainedItemTests(ServerFixture servers) : base(servers) { }

        private static async Task<ItemInfo> EquipBladeAsync(Player player)
        {
            var blade = await GiveAsync(player, ServerFixture.Blade.Id);
            await player.Game.EquipAsync(blade.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, blade.Id));
            return blade;
        }

        private static async Task KillAsync(Player killer, Player victim, int x, int y)
        {
            await killer.Game.AttackAsync(killer.User.Id, victim.User.Id, x, y);
            var kill = await victim.Game.WaitForInteractAsync(
                i => i.Action == InteractMessage.Kill && i.Target == victim.User.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(killer.User.Id, kill?.Sender);
        }

        [Fact]
        public async Task RedNameDeathDetainsWornGearAndRedeemPaysHunter()
        {
            await using var killer = (await this.PlayingAtAsync(ServerFixture.DesertCity, 160, 100, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.DesertCity, 161, 100, GameMaster)).Player;
            await FreePkAsync(killer);

            var blade = await EquipBladeAsync(victim);
            await CommandAsync(victim, "/pk 40");
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(Status, out var v) && v == RedName));

            await KillAsync(killer, victim, 161, 100);

            var victimInfo = await victim.Game.WaitForDetainItemInfoAsync(
                d => d.ItemId == blade.Id && d.Mode == DetainItemInfoMessage.DetainPage);
            var killerInfo = await killer.Game.WaitForDetainItemInfoAsync(
                d => d.ItemId == blade.Id && d.Mode == DetainItemInfoMessage.ClaimPage);
            Assert.NotNull(victimInfo);
            Assert.NotNull(killerInfo);
            Assert.Equal(victimInfo.DetainId, killerInfo.DetainId);
            Assert.True(victimInfo.Cost >= 10);
            Assert.Equal(250, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Position FROM item WHERE ItemID = @id", ("@id", blade.Id))));

            // NPC 4450 in Market opens the Redeem Equipment (336) and Claim Prize (337) windows
            await CommandAsync(victim, $"/tp {MarketMap} 206 225");
            Assert.NotNull(await victim.Game.WaitForActionAsync(ActionMessage.Teleport, victim.User.Id));
            await victim.Game.TalkToNpcAsync(PrizeOfficerMarket);
            var page = await victim.Game.ReadDialogAsync();
            Assert.Contains("redeem their detained equipment within 7 days", page?[0].Text);
            await victim.Game.AnswerNpcAsync(1);
            var openRedeem = await victim.Game.WaitForActionAsync(ActionMessage.OpenWindow, victim.User.Id);
            Assert.Equal(ActionMessage.DetainRedeemWindow, openRedeem?.Command);

            await victim.Game.TalkToNpcAsync(PrizeOfficerMarket);
            Assert.NotNull(await victim.Game.ReadDialogAsync());
            await victim.Game.AnswerNpcAsync(2);
            var openClaim = await victim.Game.WaitForActionAsync(ActionMessage.OpenWindow, victim.User.Id);
            Assert.Equal(ActionMessage.DetainClaimWindow, openClaim?.Command);

            // Victim redeems the blade for CPs
            await CommandAsync(victim, "/jewel 500");
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(Jewels, out var v) && v == 500));

            await victim.Game.RedeemDetainedAsync(victimInfo.DetainId);
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(Jewels, out var v) && v == 500 - victimInfo.Cost));
            Assert.NotNull(await victim.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 1));
            var victimRemoved = await victim.Game.WaitForDetainItemUpdateAsync(
                u => u.Page == 0 && u.Entries.Exists(e => e.DetainId == victimInfo.DetainId && !e.Keep));
            Assert.NotNull(victimRemoved);
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Position FROM item WHERE ItemID = @id", ("@id", blade.Id))));

            // Hunter gets ReadyToClaim ransom and claims the CPs
            var ransomInfo = await killer.Game.WaitForDetainItemInfoAsync(
                d => d.DetainId == victimInfo.DetainId && d.Mode == DetainItemInfoMessage.ReadyToClaim);
            Assert.NotNull(ransomInfo);
            Assert.Equal((int)victimInfo.Cost, ransomInfo.Ransom);

            await killer.Game.ClaimDetainedAsync(victimInfo.DetainId);
            Assert.NotNull(await killer.Game.WaitForAttribAsync(
                a => a.Id == killer.User.Id && a.Values.TryGetValue(Jewels, out var v) && v == victimInfo.Cost));
            var killerRemoved = await killer.Game.WaitForDetainItemUpdateAsync(
                u => u.Page == 1 && u.Entries.Exists(e => e.DetainId == victimInfo.DetainId && !e.Keep));
            Assert.NotNull(killerRemoved);
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM detained_item WHERE DetainID = @id", ("@id", victimInfo.DetainId))));
        }

        [Fact]
        public async Task ExpiredDetainedItemCanBeClaimedByHunter()
        {
            await using var killer = (await this.PlayingAtAsync(ServerFixture.DesertCity, 165, 100, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.DesertCity, 166, 100, GameMaster)).Player;
            await FreePkAsync(killer);

            var blade = await EquipBladeAsync(victim);
            await CommandAsync(victim, "/pk 40");
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(Status, out var v) && v == RedName));

            await KillAsync(killer, victim, 166, 100);
            var info = await killer.Game.WaitForDetainItemInfoAsync(
                d => d.ItemId == blade.Id && d.Mode == DetainItemInfoMessage.ClaimPage);
            Assert.NotNull(info);

            // Cannot claim before expiry
            await killer.Game.ClaimDetainedAsync(info.DetainId);
            Assert.NotNull(await killer.Game.WaitForTalkAsync(t => t.Text.Contains("neither expired nor been redeemed")));

            // Expire the detention in the database (8 days ago)
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE detained_item SET DetainedAt = UTC_TIMESTAMP() - INTERVAL 8 DAY WHERE DetainID = @id",
                ("@id", info.DetainId));

            // Owner can no longer redeem
            await CommandAsync(victim, "/jewel 500");
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(Jewels, out var v) && v == 500));
            await victim.Game.RedeemDetainedAsync(info.DetainId);
            Assert.NotNull(await victim.Game.WaitForTalkAsync(t => t.Text.Contains("expired")));

            // Hunter queries reward page (MsgAction 153) -> sees Days >= 8
            await killer.Game.QueryDetainedAsync(killer.User.Id, 1);
            var update = await killer.Game.WaitForDetainItemUpdateAsync(
                u => u.Page == 1 && u.Entries.Exists(e => e.DetainId == info.DetainId && e.Keep && e.Days >= 8));
            Assert.NotNull(update);

            // Hunter claims the expired equipment
            await killer.Game.ClaimDetainedAsync(info.DetainId);
            Assert.NotNull(await killer.Game.WaitForItemAsync(i => i.Id == blade.Id && i.Action == 1));
            Assert.Equal((int)killer.User.Id, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT CharacterID FROM item WHERE ItemID = @id AND Position = 0", ("@id", blade.Id))));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM detained_item WHERE DetainID = @id", ("@id", info.DetainId))));
        }

        [Fact]
        public async Task NormalNameDeathDoesNotDetainWornGear()
        {
            await using var killer = (await this.PlayingAtAsync(ServerFixture.DesertCity, 170, 100, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.DesertCity, 171, 100, GameMaster)).Player;
            await FreePkAsync(killer);

            var blade = await EquipBladeAsync(victim);
            await KillAsync(killer, victim, 171, 100);
            await SyncAsync(victim);
            await SyncAsync(killer);

            Assert.Null(await victim.Game.WaitForDetainItemInfoAsync(_ => true, Already));
            Assert.Null(await killer.Game.WaitForDetainItemInfoAsync(_ => true, Already));
            Assert.Equal(4, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Position FROM item WHERE ItemID = @id", ("@id", blade.Id))));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM detained_item WHERE TargetID = @id", ("@id", victim.User.Id))));
        }

        [Fact]
        public async Task DeletingACharacterCleansUpDetainedRows()
        {
            await using var killer = (await this.PlayingAtAsync(ServerFixture.DesertCity, 175, 100, strength: 5000)).Player;
            await using var victim = (await this.PlayingAtAsync(ServerFixture.DesertCity, 176, 100, GameMaster)).Player;
            await FreePkAsync(killer);

            var blade = await EquipBladeAsync(victim);
            await CommandAsync(victim, "/pk 40");
            Assert.NotNull(await victim.Game.WaitForAttribAsync(
                a => a.Id == victim.User.Id && a.Values.TryGetValue(Status, out var v) && v == RedName));

            await KillAsync(killer, victim, 176, 100);
            var info = await killer.Game.WaitForDetainItemInfoAsync(d => d.ItemId == blade.Id);
            Assert.NotNull(info);

            // Delete the hunter; both the detained_item row and the held item are cleaned up
            await killer.Game.SendAsync(ActionMessage.Build(killer.User.Id, ActionMessage.CharacterDelete));
            await this.Servers.WaitForEventAsync("game", "character.deleted", "name", killer.User.Name);

            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM detained_item WHERE DetainID = @id", ("@id", info.DetainId))));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE ItemID = @id", ("@id", blade.Id))));
        }
    }
}

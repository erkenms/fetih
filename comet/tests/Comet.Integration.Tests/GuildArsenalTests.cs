namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class GuildArsenalTests : IntegrationTest
    {
        private const uint GuildDirector = 10003;
        private const uint Silver = 4;
        private const uint GuildBattlePowerAttrib = 44;
        private const uint WeaponPole = 2;
        private const uint HeadgearPole = 0;
        private const byte ArsenalRankType = 4;
        private const byte TotalRankType = 9;

        public GuildArsenalTests(ServerFixture servers) : base(servers) { }

        private static string NewGuildName() => "A" + Guid.NewGuid().ToString("N")[..8];

        private async Task<(Player Player, string Email, uint GuildId)> FoundFundedGuildAsync(int x, int y, uint fund)
        {
            var (player, email) = await this.PlayingAtAsync(1002, x, y, GameMaster, level: 90);
            await CommandAsync(player, $"/silver {1_000_000 + fund}");
            Assert.NotNull(await player.Game.WaitForAttribAsync(
                a => a.Values.TryGetValue(Silver, out var v) && v == 1_001_000 + fund));

            var name = NewGuildName();
            await player.Game.TalkToNpcAsync(GuildDirector);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(1);
            Assert.NotNull(await player.Game.ReadDialogAsync());
            await player.Game.AnswerNpcAsync(10, name);
            var info = await player.Game.WaitForGuildInfoAsync(i => i.GuildId != 0);
            Assert.NotNull(info);

            if (fund > 0)
            {
                await player.Game.SyndicateAsync(SyndicateMessage.DonateSilver, fund);
                var donated = await player.Game.WaitForGuildInfoAsync(i => i.Fund == 500_000ul + fund);
                Assert.NotNull(donated);
            }
            return (player, email, info.GuildId);
        }

        [Fact]
        public async Task UnlockInscribeRestrictionsBonusRemoveAndRelogPersist()
        {
            var (leader, leaderEmail, guildId) = await this.FoundFundedGuildAsync(398, 396, 10_000_000);
            await using var member = (await this.PlayingAtAsync(1002, 399, 396, GameMaster, level: 90)).Player;

            // Recruit member into the guild
            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id));
            await leader.Game.SyndicateAsync(SyndicateMessage.InviteJoin, member.User.Id);
            Assert.NotNull(await member.Game.WaitForSyndicateAsync(
                s => s.Action == SyndicateMessage.InviteJoin && s.Data == leader.User.Id));
            await member.Game.SyndicateAsync(SyndicateMessage.ApplyJoin, leader.User.Id);
            Assert.NotNull(await member.Game.WaitForGuildInfoAsync(i => i.GuildId == guildId));

            // Initial query: all 8 poles locked, 0 BP
            var initial = await leader.Game.QueryArsenalAsync();
            Assert.NotNull(initial);
            Assert.Equal(8, initial.Poles.Count);
            Assert.All(initial.Poles, p => Assert.False(p.Open));
            Assert.Equal(0u, initial.TotalBattlePower);

            // Unlock Weapon arsenal (costs 5,000,000 from guild fund)
            await leader.Game.TotemPoleAsync(TotemPoleMessage.Unlock, WeaponPole);
            var unlocked = await leader.Game.WaitForTotemPoleInfoAsync(
                i => i.Poles.Any(p => p.Type == WeaponPole && p.Open));
            Assert.NotNull(unlocked);
            var afterUnlock = await leader.Game.WaitForGuildInfoAsync(i => i.Fund == 5_500_000ul);
            Assert.NotNull(afterUnlock);

            // Second unlock on the same day is rejected by the daily limit
            await leader.Game.TotemPoleAsync(TotemPoleMessage.Unlock, HeadgearPole);
            var dailyRejected = await this.Servers.WaitForEventAsync("game", "guild.rejected", "name", leader.User.Name);
            Assert.Equal("daily_limit", dailyRejected.GetProperty("reason").GetString());

            // Give leader 2 Super Blades and upgrade them to +12 with 2 sockets in DB before relog,
            // plus a normal Blade (which must be rejected for quality < Elite).
            var super1 = await GiveAsync(leader, ServerFixture.SuperBlade.Id);
            var super2 = await GiveAsync(leader, ServerFixture.SuperBlade.Id);
            var normal = await GiveAsync(leader, ServerFixture.Blade.Id);

            await this.LogOutAsync(leader);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `item` SET `Plus` = 12, `Gem1` = 255, `Gem2` = 255 WHERE `ItemID` IN (@a, @b)",
                ("@a", super1.Id), ("@b", super2.Id));

            leader = await this.Servers.PlayAsync(leaderEmail, Password);
            // Normal blade rejected for quality
            await leader.Game.TotemPoleAsync(TotemPoleMessage.Inscribe, WeaponPole, normal.Id);
            var qualityRejected = await this.Servers.WaitForEventAsync("game", "guild.rejected", "reason", "quality");
            Assert.Equal(leader.User.Name, qualityRejected.GetProperty("name").GetString());

            // Inscribe first Super +12 2-socket blade (1,362,330 donation -> 0 BP so far)
            await leader.Game.TotemPoleAsync(TotemPoleMessage.Inscribe, WeaponPole, super1.Id);
            var echo1 = await leader.Game.WaitForTotemPoleAsync(
                t => t.Action == TotemPoleMessage.Inscribe && t.ItemId == super1.Id);
            Assert.NotNull(echo1);
            var info1 = await leader.Game.WaitForTotemPoleInfoAsync(i => i.PlayerDonation == 1_362_330u);
            Assert.NotNull(info1);
            Assert.Equal(0u, info1.TotalBattlePower);

            // Inscribed item cannot be dropped
            await leader.Game.DropAsync(super1.Id);
            var dropRejected = await this.Servers.WaitForEventAsync("game", "item.ground_rejected", "name", leader.User.Name);
            Assert.Equal("inscribed", dropRejected.GetProperty("reason").GetString());

            // Inscribe second Super +12 2-socket blade (2,724,660 total -> 1 BP!)
            await leader.Game.TotemPoleAsync(TotemPoleMessage.Inscribe, WeaponPole, super2.Id);
            var info2 = await leader.Game.WaitForTotemPoleInfoAsync(
                i => i.PlayerDonation == 2_724_660u && i.TotalBattlePower == 1u);
            Assert.NotNull(info2);
            Assert.Equal(1u, info2.SharedBattlePower);
            var bpAttrib = await leader.Game.WaitForAttribAsync(
                a => a.Values.TryGetValue(GuildBattlePowerAttrib, out var v) && v == 1);
            Assert.NotNull(bpAttrib);

            // Query per-arsenal item list (MsgWeaponsInfo)
            var weapons = await leader.Game.QueryArsenalWeaponsAsync(WeaponPole, 1, 8);
            Assert.NotNull(weapons);
            Assert.Equal(2u, weapons.TotalCount);
            Assert.Equal(2, weapons.Items.Count);
            Assert.Equal(1u, weapons.BattlePower);
            Assert.Equal(2_724_660u, weapons.Donation);
            Assert.Equal(1_362_330u, weapons.Items[0].Donation);
            Assert.Equal(leader.User.Name, weapons.Items[0].OwnerName);

            // Guild donation ranking includes Arsenal column and Total column
            var arsenalRank = await leader.Game.QueryGuildDonationRankingAsync(ArsenalRankType);
            Assert.NotNull(arsenalRank);
            var leaderEntry = arsenalRank.Entries.Single(e => e.CharacterId == leader.User.Id);
            Assert.Equal(2_724_660u, leaderEntry.Arsenal);
            Assert.Equal(10_000_000u + 2_724_660u, leaderEntry.Total);

            var totalRank = await leader.Game.QueryGuildDonationRankingAsync(TotalRankType);
            Assert.NotNull(totalRank);
            Assert.Equal(leader.User.Id, totalRank.Entries[0].CharacterId);

            // Relog preserves unlocked pole, inscribed items, donation, and shared BP
            await this.LogOutAsync(leader);
            await using var again = await this.Servers.PlayAsync(leaderEmail, Password);
            var afterRelog = await again.Game.QueryArsenalAsync();
            Assert.NotNull(afterRelog);
            Assert.Equal(1u, afterRelog.TotalBattlePower);
            Assert.Equal(1u, afterRelog.SharedBattlePower);
            Assert.Equal(2_724_660u, afterRelog.PlayerDonation);
            Assert.Contains(afterRelog.Poles, p => p.Type == WeaponPole && p.Open && p.Donation == 2_724_660u);

            // Removing one inscribed item drops donation below 2M and BP back to 0
            await again.Game.TotemPoleAsync(TotemPoleMessage.Remove, WeaponPole, super2.Id);
            var echoRemove = await again.Game.WaitForTotemPoleAsync(
                t => t.Action == TotemPoleMessage.Remove && t.ItemId == super2.Id);
            Assert.NotNull(echoRemove);
            var afterRemove = await again.Game.WaitForTotemPoleInfoAsync(
                i => i.PlayerDonation == 1_362_330u && i.TotalBattlePower == 0u);
            Assert.NotNull(afterRemove);
        }

        [Fact]
        public async Task LeavingGuildUninscribesMemberEquipment()
        {
            var (leader, _, guildId) = await this.FoundFundedGuildAsync(404, 396, 5_000_000);
            await using var _ = leader;
            await using var member = (await this.PlayingAtAsync(1002, 405, 396, GameMaster, level: 90)).Player;

            Assert.NotNull(await leader.Game.WaitForSpawnAsync(member.User.Id));
            await leader.Game.SyndicateAsync(SyndicateMessage.InviteJoin, member.User.Id);
            Assert.NotNull(await member.Game.WaitForSyndicateAsync(
                s => s.Action == SyndicateMessage.InviteJoin && s.Data == leader.User.Id));
            await member.Game.SyndicateAsync(SyndicateMessage.ApplyJoin, leader.User.Id);
            Assert.NotNull(await member.Game.WaitForGuildInfoAsync(i => i.GuildId == guildId));

            await leader.Game.TotemPoleAsync(TotemPoleMessage.Unlock, WeaponPole);
            Assert.NotNull(await leader.Game.WaitForTotemPoleInfoAsync(
                i => i.Poles.Any(p => p.Type == WeaponPole && p.Open)));

            var blade = await GiveAsync(member, ServerFixture.EliteBlade.Id);
            await member.Game.TotemPoleAsync(TotemPoleMessage.Inscribe, WeaponPole, blade.Id);
            Assert.NotNull(await member.Game.WaitForTotemPoleInfoAsync(i => i.PlayerDonation == 1_000u));

            // Member leaves guild -> item is automatically uninscribed and arsenal donation returns to 0
            await member.Game.SyndicateAsync(SyndicateMessage.Leave, 0);
            var removedEcho = await member.Game.WaitForTotemPoleAsync(
                t => t.Action == TotemPoleMessage.Remove && t.ItemId == blade.Id);
            Assert.NotNull(removedEcho);
            Assert.NotNull(await member.Game.WaitForGuildInfoAsync(i => i.GuildId == 0));

            var weapons = await leader.Game.QueryArsenalWeaponsAsync(WeaponPole, 1, 8);
            Assert.NotNull(weapons);
            Assert.Equal(0u, weapons.TotalCount);
            Assert.Equal(0u, weapons.Donation);
        }
    }
}

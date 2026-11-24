namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class MentorTests : IntegrationTest
    {
        public MentorTests(ServerFixture servers) : base(servers) { }

        private Task<object> RelationCountAsync(uint mentorId, uint apprenticeId) =>
            this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM mentor WHERE MentorID = @m AND ApprenticeID = @a",
                ("@m", mentorId), ("@a", apprenticeId));

        private static async Task SetLevelAsync(Player player, int level)
        {
            await CommandAsync(player, $"/level {level}");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Level, out var l) && (int)l == level));
        }

        private static async Task GiveSilverAsync(Player player, uint amount)
        {
            await CommandAsync(player, $"/silver {amount}");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Silver, out var s) && s >= amount));
        }

        [Fact]
        public async Task EnrollQueryPersistAndLeave()
        {
            await using var mentor = (await this.PlayingAtAsync(1002, 260, 300, GameMaster)).Player;
            var (apprentice, appEmail) = await this.PlayingAtAsync(1002, 261, 300, GameMaster);
            await SetLevelAsync(mentor, 75);
            await SetLevelAsync(apprentice, 40);
            await GiveSilverAsync(apprentice, 100_000);

            // Mentor invites apprentice
            await mentor.Game.GuideAsync(GuideMessage.InviteApprentice, mentor.User.Id, apprentice.User.Id);
            var prompt = await apprentice.Game.WaitForGuideAsync(g => g.Action == GuideMessage.AcceptInviteApprentice);
            Assert.Equal((mentor.User.Id, mentor.User.Name), (prompt?.Identity, prompt?.Name));

            // Apprentice accepts
            await apprentice.Game.GuideAsync(GuideMessage.AcceptInviteApprentice, mentor.User.Id, apprentice.User.Id, 1);
            var mentorInfo = await apprentice.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Mentor);
            Assert.Equal((mentor.User.Id, apprentice.User.Id, (byte)75, true, 999999u),
                (mentorInfo?.Identity, mentorInfo?.TargetId, mentorInfo?.Level, mentorInfo?.Online, mentorInfo?.BetrayHour));
            Assert.True(mentorInfo.SharedBattlePower > 0);
            var bpUpdate = await apprentice.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.BattlePower);
            Assert.Equal(mentorInfo.SharedBattlePower, bpUpdate?.SharedBattlePower);

            var appInfo = await mentor.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Apprentice);
            Assert.Equal((mentor.User.Id, apprentice.User.Id, (byte)40, true, 999999u),
                (appInfo?.Identity, appInfo?.TargetId, appInfo?.Level, appInfo?.Online, appInfo?.BetrayHour));
            Assert.Equal(apprentice.User.Name, appInfo.Strings[1]);
            Assert.Equal(1, Convert.ToInt32(await this.RelationCountAsync(mentor.User.Id, apprentice.User.Id)));

            // Info queries return current mentor and apprentice records
            await apprentice.Game.QueryGuideInfoAsync(GuideInfoMessage.Mentor);
            var queriedMentor = await apprentice.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Mentor);
            Assert.Equal(mentor.User.Name, queriedMentor?.Strings[0]);

            await mentor.Game.QueryGuideInfoAsync(GuideInfoMessage.Apprentice);
            var queriedApp = await mentor.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Apprentice);
            Assert.Equal(apprentice.User.Name, queriedApp?.Strings[1]);

            // Logout notifies mentor; re-login restores online status
            await this.LogOutAsync(apprentice);
            var offline = await mentor.Game.WaitForGuideAsync(g => g.Action == GuideMessage.ApprenticeOffline);
            Assert.Equal(apprentice.User.Name, offline?.Name);

            await using (var back = await this.Servers.PlayAsync(appEmail, Password))
            {
                var onlineAgain = await mentor.Game.WaitForGuideInfoAsync(i =>
                    i.Mode == GuideInfoMessage.Apprentice && i.TargetId == apprentice.User.Id && i.Online);
                Assert.NotNull(onlineAgain);

                // Apprentice leaves mentor (50,000 silver)
                await back.Game.GuideAsync(GuideMessage.LeaveMentor, apprentice.User.Id, mentor.User.Id, 1);
                Assert.NotNull(await back.Game.WaitForGuideAsync(g => g.Action == GuideMessage.Remove && g.Param == apprentice.User.Id));
                Assert.NotNull(await back.Game.WaitForGuideAsync(g => g.Action == GuideMessage.ClearBattlePower));
                Assert.NotNull(await mentor.Game.WaitForGuideAsync(g => g.Action == GuideMessage.Remove && g.Param == apprentice.User.Id));
            }
            Assert.Equal(0, Convert.ToInt32(await this.RelationCountAsync(mentor.User.Id, apprentice.User.Id)));
        }

        [Fact]
        public async Task LevelGapIsEnforcedAndMentorCanExpel()
        {
            await using var mentor = (await this.PlayingAtAsync(1002, 262, 300, GameMaster)).Player;
            await using var apprentice = (await this.PlayingAtAsync(1002, 263, 300, GameMaster)).Player;
            await SetLevelAsync(mentor, 70);
            await SetLevelAsync(apprentice, 50);

            // Gap of 20 is rejected at 0 rebirths (needs 30)
            await apprentice.Game.GuideAsync(GuideMessage.RequestMentor, apprentice.User.Id, mentor.User.Id);
            await this.Servers.WaitForEventAsync("game", "mentor.rejected", "name", apprentice.User.Name);
            Assert.Equal(0, Convert.ToInt32(await this.RelationCountAsync(mentor.User.Id, apprentice.User.Id)));

            // Raise mentor to 80 (gap 30) -> request succeeds
            await SetLevelAsync(mentor, 80);
            await apprentice.Game.GuideAsync(GuideMessage.RequestMentor, apprentice.User.Id, mentor.User.Id);
            var prompt = await mentor.Game.WaitForGuideAsync(g => g.Action == GuideMessage.AcceptRequestMentor);
            Assert.Equal((apprentice.User.Id, apprentice.User.Name), (prompt?.Identity, prompt?.Name));

            await mentor.Game.GuideAsync(GuideMessage.AcceptRequestMentor, apprentice.User.Id, mentor.User.Id, 1);
            Assert.NotNull(await mentor.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Apprentice));
            Assert.NotNull(await apprentice.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Mentor));

            // Mentor expels apprentice
            await mentor.Game.GuideAsync(GuideMessage.ExpelApprentice, mentor.User.Id, apprentice.User.Id, 1);
            Assert.NotNull(await mentor.Game.WaitForGuideAsync(g => g.Action == GuideMessage.Remove && g.Param == apprentice.User.Id));
            Assert.NotNull(await apprentice.Game.WaitForGuideAsync(g => g.Action == GuideMessage.Remove && g.Param == apprentice.User.Id));
            Assert.Equal(0, Convert.ToInt32(await this.RelationCountAsync(mentor.User.Id, apprentice.User.Id)));
        }

        [Fact]
        public async Task ApprenticeBlessingAccruesAndMentorClaimsReward()
        {
            await using var mentor = (await this.PlayingAtAsync(1002, 264, 300, GameMaster)).Player;
            await using var apprentice = (await this.PlayingAtAsync(1002, 265, 300, GameMaster)).Player;
            await SetLevelAsync(mentor, 80);
            await SetLevelAsync(apprentice, 20);

            await mentor.Game.GuideAsync(GuideMessage.InviteApprentice, mentor.User.Id, apprentice.User.Id);
            Assert.NotNull(await apprentice.Game.WaitForGuideAsync(g => g.Action == GuideMessage.AcceptInviteApprentice));
            await apprentice.Game.GuideAsync(GuideMessage.AcceptInviteApprentice, mentor.User.Id, apprentice.User.Id, 1);
            Assert.NotNull(await mentor.Game.WaitForGuideInfoAsync(i => i.Mode == GuideInfoMessage.Apprentice));

            // Apprentice uses a 3-day Praying Stone (72 hours) -> 10% = 7 hours for the mentor
            await PrayAsync(apprentice, ServerFixture.SmallPrayingStone.Id);
            var accrued = await mentor.Game.WaitForContributeAsync(c =>
                c.Action == ContributeMessage.Rewards && c.Blessing == 7);
            Assert.NotNull(accrued);

            // Query current apprentice contributions (MsgContribute 6 -> 7, 6, 8)
            await mentor.Game.ContributeAsync(ContributeMessage.Apprentices);
            Assert.NotNull(await mentor.Game.WaitForContributeAsync(c => c.Action == ContributeMessage.ResetCurrent));
            var entry = await mentor.Game.WaitForContributeAsync(c => c.Action == ContributeMessage.Apprentices);
            Assert.Equal((0u, (ushort)7, apprentice.User.Name), (entry?.Generation, entry?.Blessing, entry?.Strings[1]));
            Assert.NotNull(await mentor.Game.WaitForContributeAsync(c => c.Action == ContributeMessage.Refresh));

            // Mentor claims the 7 hours of Heaven Blessing
            await mentor.Game.ContributeAsync(ContributeMessage.ClaimBlessing);
            var blessed = await mentor.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(HeavenBlessing, out var s) && s >= 6 * 3600);
            Assert.NotNull(blessed);
            var after = await mentor.Game.WaitForContributeAsync(c =>
                c.Action == ContributeMessage.Rewards && c.Blessing == 0);
            Assert.NotNull(after);
        }
    }
}

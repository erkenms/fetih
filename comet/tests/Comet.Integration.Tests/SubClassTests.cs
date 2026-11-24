namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class SubClassTests : IntegrationTest
    {
        private const uint LeadPerformer = 7776, LeadMartialArtist = 7778, WranglerNpc = 8616;
        private const byte MartialArtist = 1, Performer = 6, Wrangler = 9;
        private const byte JoinOption = 1, PromoteOption = 2;

        public SubClassTests(ServerFixture servers) : base(servers) { }

        private static async Task<string> TalkAsync(Player player, uint npc)
        {
            await player.Game.TalkToNpcAsync(npc);
            return (await player.Game.ReadDialogAsync())?[0].Text;
        }

        private static async Task<string> AnswerAsync(Player player, byte option)
        {
            await player.Game.AnswerNpcAsync(option);
            return (await player.Game.ReadDialogAsync())?[0].Text;
        }

        // Switching and promoting also push the stat window, so wait for the one that matches
        private static async Task<StatWindow> RequestStatsAsync(Player player, Func<StatWindow, bool> until = null)
        {
            await player.Game.SendAsync(StatWindow.Build(player.User.Id));
            return await player.Game.WaitForStatsAsync(s => s.Id == player.User.Id && (until == null || until(s)));
        }

        [Fact]
        public async Task LearnsUpgradesPromotesActivatesAndPersistsAcrossRelog()
        {
            var (player, email) = await this.PlayingAtAsync(ServerFixture.JobCenter, 36, 46, GameMaster, level: 69);
            try
            {
                // Level 69 is rejected at Lead Martial Artist
                Assert.Contains("level 70", await TalkAsync(player, LeadMartialArtist));
                Assert.Contains("level 70", await AnswerAsync(player, JoinOption));

                // Level 70 without 5 Celestial Stones is rejected
                await CommandAsync(player, "/level 70");
                Assert.Contains("5 Celestial Stone", await AnswerAsync(player, JoinOption));

                // Give 5 Celestial Stones and join Martial Artist
                await CommandAsync(player, $"/item {ServerFixture.CelestialStone.Id} 5");
                Assert.Contains("Phase 1", await AnswerAsync(player, JoinOption));
                var learned = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Learn);
                Assert.NotNull(learned);
                Assert.Equal(MartialArtist, learned.SubClass);
                Assert.Equal(1, learned.Phase);

                // All 5 Celestial Stones were consumed
                Assert.Equal(0L, (long)await this.Servers.QueryGameScalarAsync(
                    "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @t",
                    ("@id", player.User.Id), ("@t", ServerFixture.CelestialStone.Id)));

                // Learning alone gives no bonus; the active sub-class does
                Assert.NotNull(await RequestStatsAsync(player, s => s.CriticalStrike == 0));
                await player.Game.SendSubProAsync(SubProMessage.RequestSwitch, MartialArtist);
                var activated = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Switch);
                Assert.NotNull(activated);
                Assert.Equal(MartialArtist, activated.SubClass);

                // Stat window reflects P1 Martial Artist (+1% Critical Strike = 100 in 0.01% units)
                Assert.NotNull(await RequestStatsAsync(player, s => s.CriticalStrike == 100));

                // StudyBook gives 50 study points; /study gives 300 more (total 350)
                var book = await GiveAsync(player, ServerFixture.StudyBookArena.Id);
                await player.Game.UseAsync(book.Id);
                var sp1 = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.StudyPointsAction && m.AddedStudyPoints == 50);
                Assert.NotNull(sp1);
                Assert.Equal(0UL, sp1.StudyPoints);

                await CommandAsync(player, "/study 300");
                var sp2 = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.StudyPointsAction && m.AddedStudyPoints == 300);
                Assert.NotNull(sp2);
                Assert.Equal(50UL, sp2.StudyPoints);

                // Upgrade Martial Artist from level 1 -> 2 (costs 300 study points, leaves 50)
                await player.Game.SendSubProAsync(SubProMessage.RequestUpgrade, MartialArtist);
                var spAfter = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.StudyPointsAction && m.StudyPoints == 50);
                Assert.NotNull(spAfter);
                var upgraded = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Upgrade);
                Assert.NotNull(upgraded);
                Assert.Equal(MartialArtist, upgraded.SubClass);

                // Promote to Phase 2 requires character level 90
                await player.Game.SendSubProAsync(SubProMessage.RequestPromote, MartialArtist);
                var refused = await this.Servers.WaitForEventAsync("game", "subclass.rejected", e =>
                    e.GetProperty("name").GetString() == player.User.Name &&
                    e.GetProperty("reason").GetString() == "level");
                Assert.Equal("level", refused.GetProperty("reason").GetString());

                await CommandAsync(player, "/level 90");
                Assert.Contains("Phase 2", await AnswerAsync(player, PromoteOption));
                var promoted = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Promote);
                Assert.NotNull(promoted);
                Assert.Equal(MartialArtist, promoted.SubClass);
                Assert.Equal(2, promoted.Phase);

                // Stat window now reflects P2 Martial Artist (+2% Critical Strike = 200)
                Assert.NotNull(await RequestStatsAsync(player, s => s.CriticalStrike == 200));

                // Request info list (action 6 -> action 7 Show); the empty one from login may still be queued
                await player.Game.SendSubProAsync(SubProMessage.RequestInfo, 0);
                var shown = await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Show && m.Entries.Count > 0);
                Assert.NotNull(shown);
                Assert.Equal(50UL, shown.StudyPoints);
                var entry = Assert.Single(shown.Entries);
                Assert.Equal(MartialArtist, entry.SubClass);
                Assert.Equal(2, entry.Phase);
                Assert.Equal(2, entry.Level);
            }
            finally
            {
                await this.LogOutAsync(player);
            }

            // Relog: MsgUserInfo has ActiveSubClass and AllSubProf; MsgSubPro Show has persisted rows
            await using var relogged = await this.Servers.PlayAsync(email, Password);
            Assert.Equal(MartialArtist, relogged.User.ActiveSubClass);
            Assert.Equal(2UL, relogged.User.AllSubProf);

            await relogged.Game.SendSubProAsync(SubProMessage.RequestInfo, 0);
            var afterRelog = await relogged.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Show);
            Assert.NotNull(afterRelog);
            Assert.Equal(50UL, afterRelog.StudyPoints);
            var persisted = Assert.Single(afterRelog.Entries);
            Assert.Equal(MartialArtist, persisted.SubClass);
            Assert.Equal(2, persisted.Phase);
            Assert.Equal(2, persisted.Level);

            // Deactivate (recover -> 0)
            await relogged.Game.SendSubProAsync(SubProMessage.RequestSwitch, 0);
            var deactivated = await relogged.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Switch);
            Assert.NotNull(deactivated);
            Assert.Equal(0, deactivated.SubClass);
        }

        [Fact]
        public async Task PerformerAndWranglerApplyAttackAndHealthBonuses()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.JobCenter, 38, 36, GameMaster, level: 70)).Player;

            var before = await RequestStatsAsync(player);
            Assert.NotNull(before);

            await CommandAsync(player, $"/item {ServerFixture.Orchid.Id} 15");
            Assert.Contains("Performer", await TalkAsync(player, LeadPerformer));
            Assert.Contains("Phase 1", await AnswerAsync(player, JoinOption));
            Assert.NotNull(await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Learn && m.SubClass == Performer));
            await player.Game.SendSubProAsync(SubProMessage.RequestSwitch, Performer);
            Assert.NotNull(await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Switch && m.SubClass == Performer));

            var afterPerformer = await RequestStatsAsync(player, s => s.MinAttack == before.MinAttack + 100);
            Assert.NotNull(afterPerformer);
            Assert.Equal(before.MaxAttack + 100, afterPerformer.MaxAttack);
            Assert.Equal(before.MagicAttack + 100, afterPerformer.MagicAttack);

            await CommandAsync(player, "/tp 1004 63 36");
            await CommandAsync(player, $"/item {ServerFixture.Saddle.Id} 40");
            Assert.Contains("Wrangler", await TalkAsync(player, WranglerNpc));
            Assert.Contains("Phase 1", await AnswerAsync(player, JoinOption));
            Assert.NotNull(await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Learn && m.SubClass == Wrangler));

            Assert.Equal(afterPerformer.MaxHealth, before.MaxHealth);

            // Switching to Wrangler moves the bonus from attack to Max HP
            await player.Game.SendSubProAsync(SubProMessage.RequestSwitch, Wrangler);
            Assert.NotNull(await player.Game.WaitForSubProAsync(m => m.Action == SubProMessage.Switch && m.SubClass == Wrangler));
            var afterWrangler = await RequestStatsAsync(player, s => s.MaxHealth == before.MaxHealth + 100);
            Assert.NotNull(afterWrangler);
            Assert.Equal(before.MinAttack, afterWrangler.MinAttack);
            Assert.Equal(before.MagicAttack, afterWrangler.MagicAttack);
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class ProgressTests : IntegrationTest
    {
        private const ushort GameMaster = 3;

        public ProgressTests(ServerFixture servers) : base(servers) { }

        private static Task CommandAsync(Player player, string command) =>
            player.Game.SayAsync(TalkChannels.Talk, player.User.Name, "ALLUSERS", command);

        private static Task<UserAttribMessage> AttribAsync(Player player, uint type, ulong value) =>
            player.Game.WaitForAttribAsync(a =>
                a.Id == player.User.Id && a.Values.TryGetValue(type, out var v) && v == value);

        [Fact]
        public async Task ExperienceLevelsUpAndIsKeptAfterLogout()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 620, 520, GameMaster);
            await using var observer = (await this.PlayingAtAsync(1002, 622, 520)).Player;

            // Level 1 needs 59 and level 2 needs 195
            await CommandAsync(player, "/exp 264");
            // One attribute per packet, in this order
            Assert.NotNull(await AttribAsync(player, Level, 3));
            Assert.NotNull(await AttribAsync(player, Experience, 10));
            Assert.NotNull(await AttribAsync(player, AttributePoints, 6));
            var maxHealth = await player.Game.WaitForAttribAsync(a => a.Values.ContainsKey(MaxHealth));
            Assert.NotNull(await AttribAsync(player, Health, maxHealth.Values[MaxHealth]));
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.LevelUp, player.User.Id));
            Assert.NotNull(await observer.Game.WaitForActionAsync(ActionMessage.LevelUp, player.User.Id));

            var logged = await this.Servers.WaitForEventAsync("game", "character.level", "name", player.User.Name);
            Assert.Equal((1, 3, "gm"), (logged.GetProperty("fromLevel").GetInt32(),
                logged.GetProperty("_level").GetInt32(), logged.GetProperty("reason").GetString()));

            await CommandAsync(player, "/exp 5");
            Assert.NotNull(await AttribAsync(player, Experience, 15));

            await player.DisposeAsync();
            await this.Servers.WaitForLogoutSaveAsync(player.User.Name);
            await using var again = await this.Servers.PlayAsync(email, Password);
            Assert.Equal((3, 15UL, 6), (again.User.Level, again.User.Experience, again.User.AttributePoints));
        }

        [Fact]
        public async Task AttributePointsAreSpentOnlyWhileThereAreAny()
        {
            await using var player = (await this.PlayingAtAsync(1002, 640, 520, GameMaster)).Player;
            var user = player.User;
            await CommandAsync(player, "/level 2");
            Assert.NotNull(await AttribAsync(player, AttributePoints, 3));

            // One attribute per packet, in this order
            await player.Game.AllotAsync(1, 0, 2, 0);
            Assert.NotNull(await AttribAsync(player, Strength, user.Strength + 1UL));
            Assert.NotNull(await AttribAsync(player, Agility, user.Agility));
            Assert.NotNull(await AttribAsync(player, Vitality, user.Vitality + 2UL));
            Assert.NotNull(await AttribAsync(player, Spirit, user.Spirit));
            Assert.NotNull(await AttribAsync(player, AttributePoints, 0));
            ulong maxHealth = (ulong)((user.Vitality + 2) * 24 + (user.Strength + 1 + user.Agility + user.Spirit) * 3);
            Assert.NotNull(await AttribAsync(player, MaxHealth, maxHealth));
            Assert.NotNull(await AttribAsync(player, Health, maxHealth));

            await player.Game.AllotAsync(1, 0, 0, 0);
            var rejected = await this.Servers.WaitForEventAsync(
                "game", "character.allot_rejected", "name", user.Name);
            Assert.Equal(0, rejected.GetProperty("available").GetInt32());
            Assert.NotNull(await AttribAsync(player, Strength, user.Strength + 1UL));
        }

        [Fact]
        public async Task LevelsStopAtTheEndOfTheExperienceTable()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 520, GameMaster)).Player;
            await CommandAsync(player, "/level 121");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("Levels go from 1 to 120")));

            await CommandAsync(player, "/level 120");
            Assert.NotNull(await AttribAsync(player, Level, 120));
            await CommandAsync(player, "/exp 1000");
            Assert.NotNull(await AttribAsync(player, Experience, 0));
        }

        [Fact]
        public async Task SilverChangesAreSentAndLogged()
        {
            await using var player = (await this.PlayingAtAsync(1002, 680, 520, GameMaster)).Player;
            await CommandAsync(player, "/silver 500");
            Assert.NotNull(await AttribAsync(player, Silver, 1500));
            var logged = await this.Servers.WaitForEventAsync("game", "character.silver", "name", player.User.Name);
            Assert.Equal((500, 1500), (logged.GetProperty("amount").GetInt32(), logged.GetProperty("balance").GetInt32()));

            await CommandAsync(player, "/silver -5000");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("Silver must stay")));
        }

        [Fact]
        public async Task OnlineCharactersAreSavedPeriodically()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 520, GameMaster)).Player;
            await CommandAsync(player, "/silver 250");
            Assert.NotNull(await AttribAsync(player, Silver, 1250));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(ServerFixture.SaveIntervalSeconds * 5);
            while (Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT Silver FROM `character` WHERE Name = @name", ("@name", player.User.Name))) != 1250)
            {
                Assert.True(DateTime.UtcNow < deadline, "The character wasn't saved while online");
                await Task.Delay(100);
            }
            await this.Servers.WaitForEventAsync("game", "character.save_round", _ => true);
        }
    }
}

namespace Fetih.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    public class ProgressTests : IntegrationTest
    {
        public ProgressTests(ServerFixture servers) : base(servers) { }

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
            var levelled = await AttribAsync(player, Level, 3);
            Assert.Equal((10UL, 6UL), (levelled.Values[Experience], levelled.Values[AttributePoints]));
            Assert.Equal(levelled.Values[MaxHealth], levelled.Values[Health]);
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.LevelUp, player.User.Id));
            // Others see the halo as a named role effect: they ignore MsgAction 92
            Assert.NotNull(await observer.Game.WaitForNameAsync(n => n.Action == NameMessage.RoleEffect &&
                n.Data == player.User.Id && n.Strings.Count == 1 && n.Strings[0] == "LevelUp"));

            var logged = await this.Servers.WaitForEventAsync("game", "character.level", "name", player.User.Name);
            Assert.Equal((1, 3, "gm"), (logged.GetProperty("fromLevel").GetInt32(),
                logged.GetProperty("_level").GetInt32(), logged.GetProperty("reason").GetString()));

            await CommandAsync(player, "/exp 5");
            Assert.NotNull(await AttribAsync(player, Experience, 15));

            await this.LogOutAsync(player);
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

            await player.Game.AllotAsync(1, 0, 2, 0);
            var allotted = await AttribAsync(player, AttributePoints, 0);
            Assert.Equal((user.Strength + 1UL, (ulong)user.Agility, user.Vitality + 2UL, (ulong)user.Spirit),
                (allotted.Values[Strength], allotted.Values[Agility], allotted.Values[Vitality], allotted.Values[Spirit]));
            ulong maxHealth = (ulong)((user.Vitality + 2) * 24 + (user.Strength + 1 + user.Agility + user.Spirit) * 3);
            Assert.Equal(maxHealth, allotted.Values[MaxHealth]);
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
        public async Task JewelChangesAreSentAndLogged()
        {
            await using var player = (await this.PlayingAtAsync(1002, 685, 520, GameMaster)).Player;
            await CommandAsync(player, "/jewel 500");
            Assert.NotNull(await AttribAsync(player, Jewels, 500));
            var logged = await this.Servers.WaitForEventAsync("game", "character.conquer_points", "name", player.User.Name);
            Assert.Equal((500, 500), (logged.GetProperty("amount").GetInt32(), logged.GetProperty("balance").GetInt32()));

            await CommandAsync(player, "/jewel -5000");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("Jewels must stay")));
        }

        [Fact]
        public async Task OnlineCharactersAreSavedPeriodically()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 520, GameMaster)).Player;
            await CommandAsync(player, "/silver 250");
            Assert.NotNull(await AttribAsync(player, Silver, 1250));
            // The first save logged from here may have read the character before the command;
            // the one after it can't have
            int mark = this.Servers.Mark();
            for (int save = 0; save < 2; save++)
            {
                await this.Servers.WaitForEventAsync("game", "character.saved", e =>
                    e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("reason").GetString() == "periodic",
                    TimeSpan.FromSeconds(ServerFixture.SaveIntervalSeconds * 3), mark);
                mark = this.Servers.Mark();
                if (Convert.ToUInt32(await this.Servers.QueryGameScalarAsync(
                    "SELECT Silver FROM `character` WHERE Name = @name", ("@name", player.User.Name))) == 1250)
                    return;
            }
            Assert.Fail("The character wasn't saved with its silver while online");
        }
    }
}

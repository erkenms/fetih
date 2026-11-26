namespace Fetih.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;
    using static Fetih.TestClient.UserAttribMessage;

    public class ReincarnationTests : IntegrationTest
    {
        private const uint Seer = 46;
        private const uint ReincarnationWindow = 485;

        public ReincarnationTests(ServerFixture servers) : base(servers) { }

        private Task<long> CharacterAsync(Player player, string column) =>
            this.Servers.QueryGameScalarAsync($"SELECT {column} FROM `character` WHERE CharacterID = @id",
                ("@id", player.User.Id)).ContinueWith(t => Convert.ToInt64(t.Result));

        private async Task<Player> PlayingAsAsync(int x, int y, byte level, string set)
        {
            var (first, email) = await this.PlayingAtAsync(ServerFixture.Market, x, y, GameMaster, level);
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync($"UPDATE `character` SET {set}, X = @x, Y = @y WHERE Name = @name",
                ("@x", x), ("@y", y), ("@name", first.User.Name));
            return await this.Servers.PlayAsync(email, Password);
        }

        [Fact]
        public async Task SeerOpensTheReincarnationPanel()
        {
            await using var player = (await this.PlayingAtAsync(ServerFixture.Market, 294, 214)).Player;
            await player.Game.TalkToNpcAsync(Seer);
            var page = await player.Game.ReadDialogAsync();
            Assert.NotNull(page);
            Assert.Contains("Oblivion Dew", string.Concat(page.Where(p => p.Action == DialogMessage.Line).Select(p => p.Text)));

            await player.Game.AnswerNpcAsync(1);
            var open = await player.Game.WaitForActionAsync(ActionMessage.OpenWindow, player.User.Id);
            Assert.NotNull(open);
            Assert.Equal(ReincarnationWindow, open.Command);
        }

        [Fact]
        public async Task ReincarnationRejectsWrongRebirthsRankLevelAndMissingDew()
        {
            // Not 2nd reborn
            await using var firstReborn = await this.PlayingAsAsync(295, 214, 110,
                "CurrentClass = 25, PreviousClass = 15, Rebirths = 1");
            await GiveAsync(firstReborn, ServerFixture.OblivionDew.Id);
            await firstReborn.Game.ReincarnateAsync(11, 3);
            var r1 = await firstReborn.Game.WaitForReincarnationAsync();
            Assert.NotNull(r1);
            Assert.Equal(MeteSpecialMessage.NotSecondReborn, r1.Profession);

            // Not max rank
            await using var lowRank = await this.PlayingAsAsync(296, 214, 110,
                "CurrentClass = 24, PreviousClass = 15, AncestorClass = 15, Rebirths = 2");
            await GiveAsync(lowRank, ServerFixture.OblivionDew.Id);
            await lowRank.Game.ReincarnateAsync(11, 3);
            var r2 = await lowRank.Game.WaitForReincarnationAsync();
            Assert.NotNull(r2);
            Assert.Equal(MeteSpecialMessage.NotMaxRank, r2.Profession);

            // Level below 110
            await using var lowLevel = await this.PlayingAsAsync(297, 214, 109,
                "CurrentClass = 25, PreviousClass = 15, AncestorClass = 15, Rebirths = 2");
            await GiveAsync(lowLevel, ServerFixture.OblivionDew.Id);
            await lowLevel.Game.ReincarnateAsync(11, 3);
            var r3 = await lowLevel.Game.WaitForReincarnationAsync();
            Assert.NotNull(r3);
            Assert.Equal(MeteSpecialMessage.LevelLow, r3.Profession);

            // Missing Oblivion Dew
            await using var noDew = await this.PlayingAsAsync(298, 214, 110,
                "CurrentClass = 25, PreviousClass = 15, AncestorClass = 15, Rebirths = 2");
            await noDew.Game.ReincarnateAsync(11, 3);
            var r4 = await noDew.Game.WaitForReincarnationAsync();
            Assert.NotNull(r4);
            Assert.Equal(MeteSpecialMessage.NoItem, r4.Profession);
        }

        [Fact]
        public async Task SecondRebornMasterReincarnatesForOblivionDewAndRestoresSkillsAt110()
        {
            // Ancestor=15 (Trojan), Previous=45 (Archer), Current=25 (Warrior), Male (1003)
            await using var player = await this.PlayingAsAsync(294, 215, 110,
                "CurrentClass = 25, PreviousClass = 45, AncestorClass = 15, Rebirths = 2, FirstLifeLevel = 120, Mesh = 1003");
            await CommandAsync(player, $"/learn {ServerFixture.Cyclone.Type}");
            await CommandAsync(player, $"/learn {ServerFixture.Thunder.Type} 1");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Thunder.Type && m.Level == 1));
            var helmet = await GiveAsync(player, ServerFixture.HelmetLevelUp.Id);
            await player.Game.EquipAsync(helmet.Id, 1);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id));

            var dew = await GiveAsync(player, ServerFixture.OblivionDew.Id);
            await player.Game.ReincarnateAsync(11, 4); // Trojan, MuscularMale (1004)

            var reply = await player.Game.WaitForReincarnationAsync();
            Assert.NotNull(reply);
            Assert.Equal(MeteSpecialMessage.Success, reply.Profession);

            var attrib = await player.Game.WaitForAttribAsync(a =>
                a.Id == player.User.Id && a.Values.TryGetValue(Level, out var l) && l == 15);
            Assert.NotNull(attrib);
            Assert.Equal(11ul, attrib.Values[Class]);
            // Dropped first-life skill Cyclone forgotten; Thunder restarted to 0; Bless learned; helmet lowered
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.SpellRemove, player.User.Id));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Thunder.Type && m.Level == 0));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Bless.Type));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.TypeId == ServerFixture.Helmet.Id));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, dew.Id));

            // Reaching level 110 restores Thunder back to its previous level (1)
            await CommandAsync(player, "/level 110");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Thunder.Type && m.Level == 1));

            await this.LogOutAsync(player);
            Assert.Equal(45, await this.CharacterAsync(player, "AncestorClass"));
            Assert.Equal(25, await this.CharacterAsync(player, "PreviousClass"));
            Assert.Equal(11, await this.CharacterAsync(player, "CurrentClass"));
            Assert.Equal(2, await this.CharacterAsync(player, "Rebirths"));
            Assert.Equal(1004, await this.CharacterAsync(player, "Mesh"));
            Assert.Equal(0L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM magic WHERE CharacterID = @id AND Type = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.Cyclone.Type))));
        }
    }
}

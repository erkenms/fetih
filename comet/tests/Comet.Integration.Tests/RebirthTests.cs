namespace Comet.Integration.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    // Eternity, Alex and CelestialTao at their places in the market in Data/npcs.json, with the
    // test magictypeop.dat of ServerFixture
    public class RebirthTests : IntegrationTest
    {
        private const uint Eternity = 300500, Alex = 3600, CelestialTao = 350050;
        private const byte Trojan = 10, Warrior = 11, ConfirmTrojan = 20, ConfirmWarrior = 21, Reallot = 1, ConfirmReallot = 2;

        public RebirthTests(ServerFixture servers) : base(servers) { }

        private static string Text(System.Collections.Generic.List<DialogMessage> page) =>
            page == null ? null : string.Concat(page.Where(p => p.Action == DialogMessage.Line).Select(p => p.Text));

        private static async Task<string> TalkAsync(Player player, uint npc)
        {
            await player.Game.TalkToNpcAsync(npc);
            return Text(await player.Game.ReadDialogAsync());
        }

        private static async Task<string> AnswerAsync(Player player, byte option)
        {
            await player.Game.AnswerNpcAsync(option);
            return Text(await player.Game.ReadDialogAsync());
        }

        private Task<long> CharacterAsync(Player player, string column) =>
            this.Servers.QueryGameScalarAsync($"SELECT {column} FROM `character` WHERE CharacterID = @id",
                ("@id", player.User.Id)).ContinueWith(t => Convert.ToInt64(t.Result));

        // Logged out with the character as the SQL leaves it, then back in at x, y: the logout
        // save puts a player in the market at its portal
        private async Task<Player> PlayingAsAsync(int x, int y, byte level, string set)
        {
            var (first, email) = await this.PlayingAtAsync(ServerFixture.Market, x, y, GameMaster, level);
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync($"UPDATE `character` SET {set}, X = @x, Y = @y WHERE Name = @name",
                ("@x", x), ("@y", y), ("@name", first.User.Name));
            return await this.Servers.PlayAsync(email, Password);
        }

        [Fact]
        public async Task ATrojanMasterIsRebornAWarriorAtLevelFifteenForACelestialStone()
        {
            await using var player = await this.PlayingAsAsync(296, 219, 120, "CurrentClass = 15");
            await CommandAsync(player, $"/learn {ServerFixture.Hercules.Type}");
            await CommandAsync(player, $"/learn {ServerFixture.Thunder.Type} 1");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Thunder.Type && m.Level == 1));
            var helmet = await GiveAsync(player, ServerFixture.HelmetLevelUp.Id);
            await player.Game.EquipAsync(helmet.Id, 1);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, helmet.Id));

            Assert.Contains("Celestial Stone", await TalkAsync(player, Eternity));
            Assert.Contains("Celestial Stone", await AnswerAsync(player, Warrior));
            Assert.Contains("Celestial Stone", await AnswerAsync(player, ConfirmWarrior));
            var refused = await this.Servers.WaitForEventAsync("game", "character.rebirth_rejected", "name", player.User.Name);
            Assert.Equal("no_item", refused.GetProperty("reason").GetString());

            await GiveAsync(player, ServerFixture.CelestialStone.Id);
            Assert.NotNull(await TalkAsync(player, Eternity));
            Assert.Contains("can't be undone", await AnswerAsync(player, Warrior));
            Assert.Contains("born again", await AnswerAsync(player, ConfirmWarrior));

            var reborn = await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                a.Values.TryGetValue(Rebirths, out var r) && r == 1);
            Assert.Equal(21ul, reborn.Values[Class]);
            Assert.Equal(15ul, reborn.Values[Level]);
            Assert.Equal(30ul, reborn.Values[AttributePoints]);
            Assert.Equal(4ul, reborn.Values[Strength]);
            Assert.NotNull(await player.Game.WaitForActionAsync(ActionMessage.SpellRemove, player.User.Id));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Thunder.Type && m.Level == 0));
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.IronShirt.Type));
            Assert.NotNull(await player.Game.WaitForItemAsync(i => i.Id == helmet.Id && i.TypeId == ServerFixture.Helmet.Id));

            await this.LogOutAsync(player);
            Assert.Equal(21, await this.CharacterAsync(player, "CurrentClass"));
            Assert.Equal(15, await this.CharacterAsync(player, "PreviousClass"));
            Assert.Equal(1, await this.CharacterAsync(player, "Rebirths"));
            Assert.Equal(120, await this.CharacterAsync(player, "FirstLifeLevel"));
            Assert.Equal(15, await this.CharacterAsync(player, "Level"));
            Assert.Equal(0L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM magic WHERE CharacterID = @id AND Type = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.Hercules.Type))));
            Assert.Equal(1L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT OldLevel FROM magic WHERE CharacterID = @id AND Type = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.Thunder.Type))));
            Assert.Equal(0L, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.CelestialStone.Id))));
            Assert.Equal((long)ServerFixture.Helmet.Id, Convert.ToInt64(await this.Servers.QueryGameScalarAsync(
                "SELECT TypeID FROM item WHERE ItemID = @id", ("@id", helmet.Id))));
        }

        [Fact]
        public async Task OnlyMastersAreRebornAndEternityOnlyOnce()
        {
            await using var player = await this.PlayingAsAsync(297, 219, 120, "CurrentClass = 14");
            await GiveAsync(player, ServerFixture.CelestialStone.Id);
            Assert.Contains("highest rank", await TalkAsync(player, Eternity));

            await CommandAsync(player, "/class 25");
            Assert.Contains("Eternity", await TalkAsync(player, Alex));
        }

        [Fact]
        public async Task AlexRebornsASecondTimeForAnExemptionTokenAndTeachesBless()
        {
            await using var player = await this.PlayingAsAsync(295, 221, 120,
                "CurrentClass = 25, PreviousClass = 15, Rebirths = 1, FirstLifeLevel = 125");
            Assert.Contains("already", await TalkAsync(player, Eternity));
            await GiveAsync(player, ServerFixture.ExemptionToken.Id);
            Assert.Contains("second time", await TalkAsync(player, Alex));
            Assert.NotNull(await AnswerAsync(player, Trojan));
            Assert.Contains("born again", await AnswerAsync(player, ConfirmTrojan));

            var reborn = await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                a.Values.TryGetValue(Rebirths, out var r) && r == 2);
            Assert.Equal(11ul, reborn.Values[Class]);
            // 30 and 15 for the first life at 125, 52 for the second at 120
            Assert.Equal(97ul, reborn.Values[AttributePoints]);
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == ServerFixture.Bless.Type));

            await this.LogOutAsync(player);
            Assert.Equal(15, await this.CharacterAsync(player, "AncestorClass"));
            Assert.Equal(25, await this.CharacterAsync(player, "PreviousClass"));
            Assert.Equal(120, await this.CharacterAsync(player, "SecondLifeLevel"));
        }

        [Fact]
        public async Task CelestialTaoPutsARebornPlayersPointsBackToAllotPastSeventy()
        {
            await using var player = await this.PlayingAsAsync(291, 203, 69,
                "Rebirths = 1, Strength = 50, Agility = 6, Vitality = 30, Spirit = 2, AttributePoints = 10");
            Assert.Contains("level 70", await TalkAsync(player, CelestialTao));

            await CommandAsync(player, "/level 70");
            Assert.NotNull(await TalkAsync(player, CelestialTao));
            Assert.Contains("Are you sure", await AnswerAsync(player, Reallot));
            Assert.Contains("allot again", await AnswerAsync(player, ConfirmReallot));
            // 88 and 10 points, less the 22 every life starts with, plus 3 for level 70
            var points = await player.Game.WaitForAttribAsync(a => a.Id == player.User.Id &&
                a.Values.TryGetValue(Strength, out var s) && s == 4);
            Assert.Equal(79ul, points.Values[AttributePoints]);
        }
    }
}

namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;
    using static Comet.Client.UserAttribMessage;

    public class ActionTests : IntegrationTest
    {
        private const ulong FlyStatus = 0x8000000;

        public ActionTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task SpellAbortXpClearsFullXpBarWithoutEcho()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 740, GameMaster)).Player;
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));

            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.SpellAbortXp));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(XpCircle, out var xp) && xp == 0));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & XpFull) == 0));

            // Repeating it when not full is a clean no-op
            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.SpellAbortXp));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.SpellAbortXp, player.User.Id, Already));
            Assert.Null(await player.Game.WaitForTalkAsync(t => t.Text.StartsWith("Missing packet"), Already));
        }

        [Fact]
        public async Task MapQueryReturnsSpawnForInViewMonsterAndPlayerAndIgnoresUnknown()
        {
            var at = ServerFixture.PheasantForMagic;
            await using var first = (await this.PlayingAtAsync(1002, at.X + 4, at.Y)).Player;
            var monster = await first.Game.WaitForMonsterAsync(m => m.X == at.X && m.Y == at.Y);
            Assert.NotNull(monster);

            await using var second = (await this.PlayingAtAsync(1002, at.X + 6, at.Y)).Player;
            Assert.NotNull(await first.Game.WaitForSpawnAsync(second.User.Id));

            await first.Game.SendAsync(ActionMessage.Build(first.User.Id, ActionMessage.MapQuery, monster.Id));
            var reMonster = await first.Game.WaitForSpawnAsync(monster.Id);
            Assert.NotNull(reMonster);
            Assert.Equal((monster.X, monster.Y), (reMonster.X, reMonster.Y));

            await first.Game.SendAsync(ActionMessage.Build(first.User.Id, ActionMessage.MapQuery, second.User.Id));
            var rePlayer = await first.Game.WaitForSpawnAsync(second.User.Id);
            Assert.NotNull(rePlayer);
            Assert.Equal(second.User.Name, rePlayer.Name);

            await first.Game.SendAsync(ActionMessage.Build(first.User.Id, ActionMessage.MapQuery, 999999));
            await SyncAsync(first);
            Assert.Null(await first.Game.WaitForActionAsync(ActionMessage.MapQuery, first.User.Id, Already));
            Assert.Null(await first.Game.WaitForTalkAsync(t => t.Text.StartsWith("Missing packet"), Already));
        }

        [Fact]
        public async Task SpellAbortTransformRevertsMesh()
        {
            await using var player = (await this.PlayingAtAsync(1002, 710, 740, GameMaster)).Player;
            uint originalMesh = player.User.Mesh;
            await CommandAsync(player, "/learn 1270");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 1270));
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));

            await player.Game.CastAsync(player.User.Id, 1270, 0, 710, 740);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Mesh, out var m) && m != originalMesh));

            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.SpellAbortTransform));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Mesh, out var m) && m == originalMesh));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.SpellAbortTransform, player.User.Id, Already));
        }

        [Fact]
        public async Task SpellAbortFlightEndsFlyStatus()
        {
            await using var player = (await this.PlayingAtAsync(1002, 720, 740, GameMaster)).Player;
            var bow = await GiveAsync(player, ServerFixture.Bow.Id);
            await player.Game.EquipAsync(bow.Id, 4);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Equipped, bow.Id));
            await CommandAsync(player, "/learn 8002");
            Assert.NotNull(await player.Game.WaitForMagicInfoAsync(m => m.Type == 8002));
            await CommandAsync(player, "/xp");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & XpFull) != 0));

            await player.Game.CastAsync(player.User.Id, 8002, 0, 720, 740);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & FlyStatus) != 0));

            await player.Game.SendAsync(ActionMessage.Build(player.User.Id, ActionMessage.SpellAbortFlight));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(Status, out var s) && (s & FlyStatus) == 0));
            await SyncAsync(player);
            Assert.Null(await player.Game.WaitForActionAsync(ActionMessage.SpellAbortFlight, player.User.Id, Already));
        }

        [Fact]
        public async Task CharacterDeleteChecksPasswordAndRemovesCharacterAndItems()
        {
            var (player, _) = await this.PlayingAtAsync(1002, 730, 740, GameMaster);
            await using var guard = player;
            uint charId = player.User.Id;
            await GiveAsync(player, ServerFixture.Blade.Id);

            await player.Game.PasswordAsync(PasswordMessage.Set, 0, 4321);
            Assert.NotNull(await player.Game.WaitForPasswordAsync(p => p.Request == PasswordMessage.Correct));

            await player.Game.SendAsync(ActionMessage.Build(charId, ActionMessage.CharacterDelete, 9999));
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text == "Incorrect secondary password."));
            Assert.Equal(1, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM `character` WHERE CharacterID = @id", ("@id", charId))));

            await player.Game.SendAsync(ActionMessage.Build(charId, ActionMessage.CharacterDelete, 4321));
            await this.Servers.WaitForEventAsync("game", "character.deleted", "name", player.User.Name);
            await this.Servers.WaitForEventAsync("game", "session.logout", "name", player.User.Name);

            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM `character` WHERE CharacterID = @id", ("@id", charId))));
            Assert.Equal(0, Convert.ToInt32(await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM `item` WHERE CharacterID = @id", ("@id", charId))));
        }
    }
}

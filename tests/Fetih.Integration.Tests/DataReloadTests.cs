namespace Fetih.Integration.Tests
{
    using System.IO;
    using System.Threading.Tasks;
    using Fetih.Shared.Models;
    using Xunit;

    // Data files changed on disk and reloaded through the game's RPC, in a corner of the test
    // Twin City no other test uses
    public class DataReloadTests : IntegrationTest
    {
        private const uint NewNpc = 99901;
        private const ushort NpcX = 120, NpcY = 600, MovedX = 124, MovedY = 604, SpawnX = 128, SpawnY = 596;

        public DataReloadTests(ServerFixture servers) : base(servers) { }

        private string NpcsFile => Path.Combine(this.Servers.DataDirectory, "npcs.json");
        private string SpawnsFile => Path.Combine(this.Servers.MapDirectory, "spawns.json");

        private Task<DataReloadResult> ReloadAsync(string file) =>
            this.Servers.CallGameRpcAsync<DataReloadResult>("ReloadData", file);

        private static string WithNpc(string npcs, ushort x, ushort y) => npcs.Replace("\"npcs\": [",
            $"\"npcs\": [\n    {{ \"id\": {NewNpc}, \"lookface\": 20, \"role\": 2, \"map\": 1002, \"x\": {x}, \"y\": {y} }},");

        [Fact]
        public async Task AnNpcAddedAndMovedThroughReloadIsShownWhereItIs()
        {
            var original = await File.ReadAllTextAsync(this.NpcsFile);
            await using var player = (await this.PlayingAtAsync(1002, NpcX, NpcY + 2)).Player;
            try
            {
                await File.WriteAllTextAsync(this.NpcsFile, WithNpc(original, NpcX, NpcY));
                var added = await ReloadAsync("npcs.json");
                Assert.True(added.Applied, string.Join("; ", added.Errors));
                var npc = await player.Game.WaitForNpcAsync(NewNpc);
                Assert.Equal((NpcX, NpcY), (npc?.X, npc?.Y));

                await File.WriteAllTextAsync(this.NpcsFile, WithNpc(original, MovedX, MovedY));
                Assert.True((await ReloadAsync("npcs.json")).Applied);
                Assert.True(await player.Game.WaitForRemovalAsync(NewNpc));
                var moved = await player.Game.WaitForNpcAsync(NewNpc);
                Assert.Equal((MovedX, MovedY), (moved?.X, moved?.Y));
            }
            finally
            {
                await File.WriteAllTextAsync(this.NpcsFile, original);
                await ReloadAsync("npcs.json");
            }

            Assert.True(await player.Game.WaitForRemovalAsync(NewNpc));
        }

        [Fact]
        public async Task ASpawnAddedThroughReloadPlacesItsMonster()
        {
            var original = await File.ReadAllTextAsync(this.SpawnsFile);
            await using var player = (await this.PlayingAtAsync(1002, SpawnX, SpawnY + 3)).Player;
            uint monsterId = 0;
            try
            {
                await File.WriteAllTextAsync(this.SpawnsFile, original.Replace("\n] }",
                    $",\n{{ \"id\": 9901, \"map\": 1002, \"x\": {SpawnX}, \"y\": {SpawnY}, \"spread\": 0, " +
                    "\"amount\": 1, \"respawn\": 2, \"monster\": 1 }\n] }"));
                var result = await ReloadAsync("spawns.json");
                Assert.True(result.Applied, string.Join("; ", result.Errors));
                var monster = await player.Game.WaitForMonsterAsync(m => m.X == SpawnX && m.Y == SpawnY);
                Assert.NotNull(monster);
                monsterId = monster.Id;
            }
            finally
            {
                await File.WriteAllTextAsync(this.SpawnsFile, original);
                await ReloadAsync("spawns.json");
            }

            Assert.True(await player.Game.WaitForRemovalAsync(monsterId));
        }

        [Fact]
        public async Task ABrokenFileChangesNothing()
        {
            var original = await File.ReadAllTextAsync(this.NpcsFile);
            await using var player = (await this.PlayingAtAsync(1002, NpcX, NpcY + 2)).Player;
            try
            {
                var twice = WithNpc(WithNpc(original, NpcX, NpcY), MovedX, MovedY);
                await File.WriteAllTextAsync(this.NpcsFile, twice);
                var result = await ReloadAsync("npcs.json");
                Assert.False(result.Applied);
                Assert.Contains(result.Errors, e => e.Contains(NewNpc.ToString()));
                await SyncAsync(player);
                Assert.Null(await player.Game.WaitForNpcAsync(NewNpc, Already));

                var unknown = await ReloadAsync("../Fetih.Game.config");
                Assert.False(unknown.Applied);
                Assert.NotEmpty(unknown.Errors);
            }
            finally
            {
                await File.WriteAllTextAsync(this.NpcsFile, original);
            }
        }
    }
}

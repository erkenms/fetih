namespace Comet.Game.Tests.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Comet.Client;
    using Comet.Game.DataEditing;
    using Comet.Game.World;
    using Comet.Game.Tests.World;
    using Xunit;

    public sealed class DataSetTests : IDisposable
    {
        private readonly string Directory = System.IO.Directory.CreateTempSubdirectory("comet-dataset-").FullName;

        // Map 1002 is 20x20 with a wall at x = 10 and portal 3 at (2, 2)
        private static readonly MapGrid Plain = MapGrid.Parse(MapFiles.DMap(20, 20,
            walkable: (x, y) => x != 10, portals: new (ushort, ushort, uint)[] { (2, 2, 3) }));

        public DataSetTests()
        {
            this.Write("monsters.json", "monsters",
                "{ \"id\": 1, \"name\": \"Pheasant\", \"life\": 33, \"attackMin\": 5, \"attackMax\": 6 }",
                "{ \"id\": 2, \"name\": \"Bad\", \"life\": 0, \"attackMin\": 9, \"attackMax\": 6, \"dropHp\": 5 }",
                "{ \"id\": 1, \"name\": \"Again\", \"life\": 10 }");
        }

        public void Dispose() => System.IO.Directory.Delete(this.Directory, true);

        private DataSet Set() => new DataSet(this.Directory, id => id == 1002 ? Plain : null, id => id is 1000000 or 1060020);

        private void Write(string name, string key, params string[] entries) =>
            File.WriteAllText(Path.Combine(this.Directory, name),
                "// test\n{\n  \"" + key + "\": [\n    " + string.Join(",\n    ", entries) + "\n  ]\n}\n");

        private static List<string> Errors(IEnumerable<DataProblem> problems) =>
            problems.Where(p => p.Level == DataProblemLevel.Error).Select(p => $"{p.Line}: {p.Message}").ToList();

        private static List<string> Warnings(IEnumerable<DataProblem> problems) =>
            problems.Where(p => p.Level == DataProblemLevel.Warning).Select(p => $"{p.Line}: {p.Message}").ToList();

        [Fact]
        public void ChecksMonsters()
        {
            var problems = this.Set().Validate("monsters.json");
            Assert.Equal(new[]
            {
                "5: Life must be above 0",
                "5: attackMin is above attackMax",
                "6: Monster ID 1 is already used on line 4",
            }, Errors(problems));
            Assert.Equal(new[] { "5: dropHp 5 isn't in the client's itemtype.dat" }, Warnings(problems));
            Assert.All(problems, p => Assert.Equal("monsters.json", p.File));
            Assert.Equal(new int?[] { 1, 1, 1, 2 }, problems.Select(p => p.Entry).ToArray());
        }

        [Fact]
        public void ChecksNpcCellsAndIdsSharedWithGoals()
        {
            this.Write("npcs.json", "npcs",
                "{ \"id\": 7, \"map\": 1002, \"x\": 10, \"y\": 1 }",
                "{ \"id\": 8, \"map\": 1002, \"x\": 25, \"y\": 1 }",
                "{ \"id\": 9, \"map\": 1003, \"x\": 1, \"y\": 1 }");
            this.Write("goals.json", "npcs", "{ \"id\": 8, \"map\": 1002, \"x\": 1, \"y\": 1 }");

            Assert.Equal(new[]
            {
                "5: NPC ID 8 is also in goals.json",
                "5: The NPC (25, 1) is outside map 1002, which is 20x20",
                "6: Map 1003 isn't in the client's GameMap.dat",
            }, Errors(this.Set().Validate("npcs.json")));
            Assert.Equal(new[] { "4: NPC ID 8 is also in npcs.json" }, Errors(this.Set().Validate("goals.json")));
        }

        [Fact]
        public void ChecksSpawns()
        {
            this.Write("spawns.json", "spawns",
                "{ \"id\": 1, \"map\": 1002, \"x\": 10, \"y\": 5, \"spread\": 3, \"amount\": 2, \"monster\": 1 }",
                "{ \"id\": 2, \"map\": 1002, \"x\": 10, \"y\": 5, \"spread\": 0, \"amount\": 0, \"monster\": 3 }",
                "{ \"id\": 2, \"map\": 1002, \"x\": 5, \"y\": 50, \"amount\": 1, \"monster\": 1 }");

            var problems = this.Set().Validate("spawns.json");
            Assert.Equal(new[]
            {
                "5: Monster 3 isn't in monsters.json",
                "6: Spawn ID 2 is already used on line 5",
                "6: The spawn's center (5, 50) is outside map 1002, which is 20x20",
            }, Errors(problems));
            Assert.Equal(new[]
            {
                "5: Amount is 0, so it spawns nothing",
                "5: The spawn's center (10, 5) on map 1002 isn't walkable",
            }, Warnings(problems));
        }

        [Fact]
        public void ChecksPortalsAgainstTheMapsPortalCells()
        {
            this.Write("portals.json", "portals",
                "{ \"map\": 1002, \"portal\": 3, \"toMap\": 1002, \"toX\": 4, \"toY\": 4 }",
                "{ \"map\": 1002, \"portal\": 4, \"toMap\": 1002, \"toX\": 10, \"toY\": 4 }",
                "{ \"map\": 1002, \"portal\": 3, \"toMap\": 1005, \"toX\": 4, \"toY\": 4 }");

            var problems = this.Set().Validate("portals.json");
            Assert.Equal(new[]
            {
                "5: Map 1002 has no portal 4 (it has 3)",
                "6: Map and portal pair (1002, 3) is already used on line 4",
                "6: Map 1005 isn't in the client's GameMap.dat",
            }, Errors(problems));
            Assert.Equal(new[] { "5: The destination (10, 4) on map 1002 isn't walkable" }, Warnings(problems));
        }

        [Fact]
        public void ChecksMapsAndOnlyCaresAboutPortalPointsInUse()
        {
            this.Write("maps.json", "maps",
                "{ \"map\": 1002, \"name\": \"twin\", \"flags\": \"RecordDisable\", \"portal\": { \"x\": 30, \"y\": 3 }, \"reborn\": 1002 }",
                "{ \"map\": 1003, \"name\": \"Twin\", \"portal\": { \"x\": 1, \"y\": 1 }, \"scroll\": 1060099 }",
                "{ \"map\": 1004, \"flags\": \"RecordDisable\", \"reborn\": 1005 }");

            var problems = this.Set().Validate("maps.json");
            Assert.Equal(new[]
            {
                "4: The portal point (30, 3) is outside map 1002, which is 20x20",
                "5: The name 'twin' is also used on line 4",
                "5: Map 1003 isn't in the client's GameMap.dat",
                "5: Scroll 1060099 isn't in the client's itemtype.dat",
                "6: Map 1004 isn't in the client's GameMap.dat",
                "6: Reborn map 1005 has no portal in this file",
            }, Errors(problems));
        }

        [Fact]
        public void ChecksDropsBossesAndTeleporters()
        {
            this.Write("drops.json", "drops",
                "{ \"monster\": 1, \"entries\": [{ \"item\": 1000000, \"chance\": 100 }, { \"item\": 5, \"chance\": 120, \"count\": 0 }] }",
                "{ \"monster\": 4, \"entries\": [] }");
            Assert.Equal(new[]
            {
                "4: Dropped item 5 isn't in the client's itemtype.dat",
                "4: Chance 120 for item 5 isn't in 0-100",
                "4: Count 0 for item 5 must be at least 1",
                "5: Monster 4 isn't in monsters.json",
            }, Errors(this.Set().Validate("drops.json")));

            this.Write("bosses.json", "bosses",
                "{ \"id\": \"a\", \"monster\": 1, \"map\": 1002, \"x\": 1, \"y\": 1, \"times\": [\"25:00\"] }",
                "{ \"id\": \"A\", \"monster\": 1, \"map\": 1002, \"x\": 1, \"y\": 1, \"intervalSeconds\": 60 }");
            Assert.Equal(new[]
            {
                "4: '25:00' isn't a time of day (HH:mm)",
                "5: Boss ID a is already used on line 4",
            }, Errors(this.Set().Validate("bosses.json")));

            this.Write("npcs.json", "npcs", "{ \"id\": 1, \"map\": 1002, \"x\": 1, \"y\": 1 }");
            this.Write("teleporters.json", "teleporters",
                "{ \"id\": 1, \"destinations\": [{ \"name\": \"Far\", \"map\": 1002, \"x\": 1, \"y\": 99 }] }",
                "{ \"id\": 2, \"destinations\": [] }");
            Assert.Equal(new[]
            {
                "4: Destination 'Far' (1, 99) is outside map 1002, which is 20x20",
                "5: NPC 2 isn't in npcs.json or goals.json",
                "5: Needs 1 to 254 destinations",
            }, Errors(this.Set().Validate("teleporters.json")));

            this.Write("goals.json", "npcs", "{ \"id\": 3, \"map\": 1002, \"x\": 2, \"y\": 2, \"role\": 21 }");
            this.Write("talkers.json", "talkers",
                "{ \"id\": 3, \"text\": [\"Hi\"], \"reply\": \"Bye\" }",
                "{ \"id\": 4, \"text\": [\"Hi\"], \"reply\": \"Bye\" }");
            Assert.Equal(new[] { "5: NPC 4 isn't in npcs.json or goals.json" }, Errors(this.Set().Validate("talkers.json")));
        }

        [Fact]
        public void ValidatesAnEditBeforeItIsSaved()
        {
            this.Write("levels.json", "levels", "{ \"level\": 1, \"experience\": 59 }", "{ \"level\": 2, \"experience\": 195 }");
            var set = this.Set();
            Assert.Empty(set.Validate("levels.json"));

            var edited = DataFileKinds.Levels.Load(Path.Combine(this.Directory, "levels.json"));
            var row = edited.GetJson(1);
            row["level"] = 1;
            row["experience"] = 0;
            edited.SetJson(1, row);
            set.Use("levels.json", edited);
            Assert.Equal(new[] { "5: Level 1 is already used on line 4", "5: Experience must be above 0" },
                Errors(set.Validate("levels.json")));
        }

        [Fact]
        public void ReportsAFileThatDoesNotParse()
        {
            File.WriteAllText(Path.Combine(this.Directory, "talkers.json"), "{ \"talkers\": [ { \"id\": 1, } ");
            var problem = Assert.Single(this.Set().Validate("talkers.json"));
            Assert.Null(problem.Entry);
            Assert.Equal(DataProblemLevel.Error, problem.Level);
        }

        [RealClientFact]
        public void TheCurrentDataHasNoErrors()
        {
            var problems = DataSet.ForClient(DataFileTests.SourceData, RealClientFactAttribute.Directory).ValidateAll();
            Assert.Empty(problems.Where(p => p.Level == DataProblemLevel.Error).Select(p => $"{p.File}:{p.Line} {p.Message}"));
        }
    }
}

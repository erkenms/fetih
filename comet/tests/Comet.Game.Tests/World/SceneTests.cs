namespace Comet.Game.Tests.World
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Comet.Client;
    using Comet.Game.World;
    using Xunit;

    public class SceneTests
    {
        private static IReadOnlyList<ScenePart> Scenes(string path) => path switch
        {
            // 3 wide, 2 high, offset (1, 0): cell (0,0) is walkable, (2,1) blocked
            "map\\Scene\\stand.scene" => SceneFile.Parse(MapFiles.Scene(3, 2, offsetX: 1,
                walkable: (x, y) => (x, y) != (2, 1), altitude: (x, y) => 10 * x + y)),
            _ => throw new InvalidDataException($"No scene {path}")
        };

        [Fact]
        public void TerrainSceneCellsReplaceTheBaseCellsBackwardsFromTheObject()
        {
            // Base grid all blocked; one terrain object at (10, 20)
            var grid = MapGrid.Parse(MapFiles.DMap(40, 40, walkable: (x, y) => false,
                terrain: new[] { ("map\\Scene\\stand.scene", 10, 20) }, covers: 2), Scenes);

            // Scene cell (i, j) lands on (10 + 1 - i, 20 + 0 - j)
            Assert.True(grid.IsWalkable(11, 20));      // (0,0)
            Assert.True(grid.IsWalkable(9, 20));       // (2,0)
            Assert.True(grid.IsWalkable(11, 19));      // (0,1)
            Assert.False(grid.IsWalkable(9, 19));      // (2,1) is blocked in the scene
            Assert.False(grid.IsWalkable(12, 20));     // outside the scene: base grid
            Assert.False(grid.IsWalkable(11, 21));
            Assert.Equal(21, grid.Elevation(9, 19));   // altitude 10 * 2 + 1
        }

        [Fact]
        public void BlockedSceneCellsBlockWalkableGround()
        {
            var grid = MapGrid.Parse(MapFiles.DMap(40, 40,
                terrain: new[] { ("map\\Scene\\stand.scene", 10, 20) }), Scenes);
            Assert.False(grid.IsWalkable(9, 19));
            Assert.True(grid.IsWalkable(9, 20));
        }

        [Fact]
        public void ScenePartsOutsideTheMapAreCut()
        {
            var grid = MapGrid.Parse(MapFiles.DMap(5, 5, walkable: (x, y) => false,
                terrain: new[] { ("map\\Scene\\stand.scene", 0, 0) }), Scenes);
            Assert.True(grid.IsWalkable(1, 0));
            Assert.True(grid.IsWalkable(0, 0));
        }

        [Fact]
        public void WithoutScenesTheBaseGridIsKept()
        {
            var grid = MapGrid.Parse(MapFiles.DMap(40, 40, walkable: (x, y) => false,
                terrain: new[] { ("map\\Scene\\stand.scene", 10, 20) }));
            Assert.False(grid.IsWalkable(11, 20));
        }

        [Fact]
        public void UnknownObjectTypesAreRejected()
        {
            var data = MapFiles.DMap(4, 4, covers: 1);
            int objectType = data.Length - 420;
            BitConverter.TryWriteBytes(data.AsSpan(objectType), 7u);
            Assert.Throws<InvalidDataException>(() => MapGrid.Parse(data, Scenes));
        }

        [Fact]
        public void ReadsASceneFile()
        {
            var part = Assert.Single(SceneFile.Parse(MapFiles.Scene(2, 3, offsetX: 4, offsetY: -1)));
            Assert.Equal((2, 3, 4, -1), (part.Width, part.Height, part.OffsetX, part.OffsetY));
            Assert.All(part.Cells, cell => Assert.True(cell.Walkable));
        }

        [Fact]
        public void RejectsATruncatedSceneFile()
        {
            var data = MapFiles.Scene(2, 2);
            Assert.Throws<InvalidDataException>(() => SceneFile.Parse(data.AsSpan(0, data.Length - 1)));
        }
    }

    /// <summary>A fact that only runs when a copy of the 5619 client is next to the comet folder.</summary>
    public sealed class RealClientFactAttribute : FactAttribute
    {
        public static readonly string Directory = Find();

        public RealClientFactAttribute()
        {
            if (Directory == null)
                this.Skip = "No _CLIENT/5619 folder above the test folder";
        }

        private static string Find()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var client = Path.Combine(dir.FullName, "_CLIENT", "5619");
                if (System.IO.Directory.Exists(Path.Combine(client, "map")))
                    return client;
            }

            return null;
        }
    }

    /// <summary>Checks map loading against the real 5619 client files.</summary>
    public class RealMapTests
    {
        [RealClientFact]
        public void EveryCellOfJumpsSeenInVmIsWalkable()
        {
            // 88 jumps on the newbie village (1010) from the VM logs: kind fromX fromY toX toY
            var grid = new MapStore(RealClientFactAttribute.Directory).Get(1010);
            var jumps = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "World", "Fixtures", "jumps-1010.txt"))
                .Where(line => line.Trim().Length > 0)
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray())
                .ToList();
            Assert.Equal(88, jumps.Count);

            var blocked = jumps
                .SelectMany(j => new[] { (j[1], j[2]), (j[3], j[4]) })
                .Distinct()
                .Where(cell => !grid.IsWalkable(cell.Item1, cell.Item2))
                .ToList();
            Assert.Empty(blocked);
        }

        [Fact]
        public void EveryJumpSeenInVmIsWithinTheJumpLimit()
        {
            // The server refuses jumps longer than this; real players must never hit it
            var longest = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "World", "Fixtures", "jumps-1010.txt"))
                .Where(line => line.Trim().Length > 0)
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray())
                .Max(j => Math.Max(Math.Abs(j[3] - j[1]), Math.Abs(j[4] - j[2])));
            Assert.InRange(longest, 1, Movement.MaxJumpDistance);
        }

        [RealClientFact]
        public void EveryListedMapWithFilesLoads()
        {
            var store = new MapStore(RealClientFactAttribute.Directory);
            var failed = new List<uint>();
            foreach (var id in store.MapIDs)
            {
                try { store.Get(id); }
                catch (InvalidDataException) { failed.Add(id); }
            }

            // The nine Kunlun maps (1712 to 1720) are listed but have no files in this client
            Assert.Equal(Enumerable.Range(1712, 9).Select(i => (uint)i), failed.OrderBy(i => i));
        }
    }
}

namespace Fetih.Game.Tests.World
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Fetih.TestClient;
    using Fetih.Game.World;
    using Xunit;

    public class GameMapIndexTests
    {
        [Fact]
        public void ReadsMapsAndPaths()
        {
            var maps = GameMapIndex.Parse(MapFiles.GameMapDat(
                (1002, "map/map/newplain.7z"), (1010, "map\\map\\newbie.7z")));
            Assert.Equal(2, maps.Count);
            Assert.Equal("map/map/newplain.7z", maps[1002].Path);
            Assert.Equal("map/map/newbie.7z", maps[1010].Path);
            Assert.Equal(256u, maps[1010].PuzzleSize);
        }

        [Fact]
        public void KeepsTheFirstOfARepeatedMap()
        {
            var maps = GameMapIndex.Parse(MapFiles.GameMapDat(
                (1067, "map/map/qiling-z.7z"), (1067, "map/map/09Christmas02.7z")),
                out var duplicates);
            Assert.Equal("map/map/qiling-z.7z", maps[1067].Path);
            Assert.Equal("map/map/09Christmas02.7z", Assert.Single(duplicates).Path);
        }

        [Fact]
        public void RejectsATruncatedFile()
        {
            var data = MapFiles.GameMapDat((1002, "map/map/newplain.7z"));
            Assert.Throws<InvalidDataException>(() => GameMapIndex.Parse(data.AsSpan(0, data.Length - 2)));
        }
    }

    public class MapGridTests
    {
        private static MapGrid Grid() => MapGrid.Parse(MapFiles.DMap(10, 6,
            walkable: (x, y) => !(x == 4 && y == 2),
            elevation: (x, y) => (short)(x * 10 + y),
            portals: new (ushort, ushort, uint)[] { (3, 5, 7) }));

        [Fact]
        public void ReadsSizeWalkabilityAndElevation()
        {
            var grid = Grid();
            Assert.Equal(10, grid.Width);
            Assert.Equal(6, grid.Height);
            Assert.True(grid.IsWalkable(0, 0));
            Assert.True(grid.IsWalkable(9, 5));
            Assert.False(grid.IsWalkable(4, 2));
            Assert.Equal(92, grid.Elevation(9, 2));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, -1)]
        [InlineData(10, 0)]
        [InlineData(0, 6)]
        public void CellsOutsideTheMapArentWalkable(int x, int y)
        {
            var grid = Grid();
            Assert.False(grid.Contains(x, y));
            Assert.False(grid.IsWalkable(x, y));
        }

        [Fact]
        public void ReadsPortals()
        {
            Assert.Equal(new MapPortal(3, 5, 7), Assert.Single(Grid().Portals));
        }

        [Theory]
        [InlineData(3, 5)]
        [InlineData(2, 4)]
        [InlineData(4, 4)]
        public void FindsAPortalFromItsCellOrOneStepAway(int x, int y)
        {
            Assert.Equal(new MapPortal(3, 5, 7), Grid().PortalNear(x, y));
        }

        [Theory]
        [InlineData(1, 5)]
        [InlineData(3, 3)]
        public void FindsNoPortalFartherAway(int x, int y)
        {
            Assert.Null(Grid().PortalNear(x, y));
        }

        [Fact]
        public void FirstPortalInTheFileWinsWhenTwoAreNear()
        {
            var grid = MapGrid.Parse(MapFiles.DMap(10, 10,
                portals: new (ushort, ushort, uint)[] { (5, 5, 2), (6, 5, 1) }));
            Assert.Equal(2u, grid.PortalNear(6, 6)?.Index);
        }

        [Fact]
        public void RejectsTruncatedCells()
        {
            var data = MapFiles.DMap(10, 6);
            Assert.Throws<InvalidDataException>(() => MapGrid.Parse(data.AsSpan(0, 300)));
        }

        [Fact]
        public void RejectsAnImpossibleSize()
        {
            var data = MapFiles.DMap(1, 1);
            BitConverter.TryWriteBytes(data.AsSpan(268), 100000u);
            Assert.Throws<InvalidDataException>(() => MapGrid.Parse(data));
        }
    }

    public sealed class MapStoreTests : IDisposable
    {
        private readonly string Directory = Path.Combine(Path.GetTempPath(), "fetih-maps-" + Guid.NewGuid().ToString("N"));

        public MapStoreTests()
        {
            var maps = Path.Combine(this.Directory, "map", "map");
            System.IO.Directory.CreateDirectory(maps);
            System.IO.Directory.CreateDirectory(Path.Combine(this.Directory, "ini"));
            File.WriteAllBytes(Path.Combine(this.Directory, "ini", "GameMap.dat"), MapFiles.GameMapDat(
                (1, "map/map/tiny.7z"), (2, "map/map/plain.7z"), (3, "map/map/missing.7z")));

            // Map 1 is a real 7z archive; map 2 only has a plain DMap next to its listed path
            File.Copy(Path.Combine(AppContext.BaseDirectory, "World", "Fixtures", "tiny.7z"),
                Path.Combine(maps, "tiny.7z"));
            File.WriteAllBytes(Path.Combine(maps, "plain.DMap"), MapFiles.DMap(20, 30));
        }

        public void Dispose() => System.IO.Directory.Delete(this.Directory, recursive: true);

        [Fact]
        public void ListsTheMaps()
        {
            var store = new MapStore(this.Directory);
            Assert.Equal(3, store.Count);
            Assert.True(store.Exists(2));
            Assert.False(store.Exists(4));
        }

        [Fact]
        public void ReadsAMapFromItsArchive()
        {
            // The fixture is 8x6, column 0 blocked, elevation x*10+y, one portal (3,4) #7
            var grid = new MapStore(this.Directory).Get(1);
            Assert.Equal((8, 6), (grid.Width, grid.Height));
            Assert.False(grid.IsWalkable(0, 3));
            Assert.True(grid.IsWalkable(1, 3));
            Assert.Equal(53, grid.Elevation(5, 3));
            Assert.Equal(new MapPortal(3, 4, 7), Assert.Single(grid.Portals));
        }

        [Fact]
        public void FallsBackToAPlainDMap()
        {
            var grid = new MapStore(this.Directory).Get(2);
            Assert.Equal((20, 30), (grid.Width, grid.Height));
        }

        [Fact]
        public void LoadsEachMapOnce()
        {
            var store = new MapStore(this.Directory);
            Assert.Same(store.Get(1), store.Get(1));
        }

        [Fact]
        public void ReportsMissingAndUnknownMaps()
        {
            var store = new MapStore(this.Directory);
            Assert.Throws<InvalidDataException>(() => store.Get(3));
            Assert.Throws<KeyNotFoundException>(() => store.Get(4));
        }

        [Fact]
        public void NeedsGameMapDat()
        {
            File.Delete(Path.Combine(this.Directory, "ini", "GameMap.dat"));
            Assert.Throws<FileNotFoundException>(() => new MapStore(this.Directory));
        }
    }
}

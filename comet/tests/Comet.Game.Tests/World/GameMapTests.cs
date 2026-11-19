namespace Comet.Game.Tests.World
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Client;
    using Comet.Game.World;
    using Xunit;

    public class GameMapTests
    {
        private sealed class Thing : IMapObject
        {
            public Thing(uint identity) => this.Identity = identity;
            public uint Identity { get; }
            public override string ToString() => this.Identity.ToString();
        }

        private static GameMap<Thing> Map(int width = 200, int height = 200) =>
            new GameMap<Thing>(1002, MapGrid.Parse(MapFiles.DMap(width, height)));

        [Fact]
        public void KnowsWhichCellsAreTaken()
        {
            var map = Map();
            var thing = new Thing(1);
            map.Add(thing, 17, 18);
            Assert.True(map.AnyAt(17, 18));
            Assert.False(map.AnyAt(18, 18));
            Assert.False(map.AnyAt(17, 17));
            map.Move(thing, 18, 18);
            Assert.False(map.AnyAt(17, 18));
            Assert.True(map.AnyAt(18, 18));
            Assert.Equal(new[] { thing }, map.All());
        }

        [Fact]
        public void SeesEveryoneWithin18CellsOnBothAxes()
        {
            var map = Map();
            var me = new Thing(1);
            map.Add(me, 100, 100);
            var near = new[]
            {
                (new Thing(2), (ushort)118, (ushort)100),
                (new Thing(3), (ushort)82, (ushort)82),
                (new Thing(4), (ushort)118, (ushort)118),
                (new Thing(5), (ushort)100, (ushort)100)
            };
            var far = new[]
            {
                (new Thing(6), (ushort)119, (ushort)100),
                (new Thing(7), (ushort)100, (ushort)81),
                (new Thing(8), (ushort)150, (ushort)150)
            };
            foreach (var (thing, x, y) in near.Concat(far))
                map.Add(thing, x, y);

            var seen = map.InView(100, 100, except: me).Select(t => t.Identity).OrderBy(i => i);
            Assert.Equal(new uint[] { 2, 3, 4, 5 }, seen);
        }

        [Fact]
        public void LooksAcrossSectorAndMapEdges()
        {
            // Sectors are 18 cells wide: (0,0) and (18,18) are in different sectors, and
            // a 40x40 map's last sector is only 4 cells wide
            var map = Map(40, 40);
            var corner = new Thing(1);
            var nextSector = new Thing(2);
            var farCorner = new Thing(3);
            map.Add(corner, 0, 0);
            map.Add(nextSector, 18, 18);
            map.Add(farCorner, 39, 39);

            // (0,0) sees (18,18) in the next sector, but not (39,39)
            Assert.Equal(new[] { nextSector }, map.InView(0, 0, except: corner));

            // (25,25) sees (18,18) and (39,39) in the short last sector, but not (0,0)
            Assert.Equal(new[] { nextSector, farCorner },
                map.InView(25, 25).OrderBy(t => t.Identity));
        }

        [Fact]
        public void MovingChangesWhoSeesWhom()
        {
            var map = Map();
            var walker = new Thing(1);
            var watcher = new Thing(2);
            map.Add(walker, 10, 10);
            map.Add(watcher, 60, 10);
            Assert.Empty(map.InView(60, 10, except: watcher));

            Assert.True(map.Move(walker, 45, 10));
            Assert.Equal(new[] { walker }, map.InView(60, 10, except: watcher));
            Assert.True(map.TryGetPosition(walker, out var x, out var y));
            Assert.Equal((45, 10), (x, y));

            Assert.True(map.Move(walker, 41, 10));
            Assert.Empty(map.InView(60, 10, except: watcher));
        }

        [Fact]
        public void RemovedObjectsAreGone()
        {
            var map = Map();
            var thing = new Thing(1);
            map.Add(thing, 50, 50);
            Assert.True(map.Remove(thing));
            Assert.Empty(map.InView(50, 50));
            Assert.Equal(0, map.Count);
            Assert.False(map.Move(thing, 51, 50));
        }

        [Fact]
        public void SameIdentityReplacesAndTheOldOneCantRemoveTheNewOne()
        {
            var map = Map();
            var old = new Thing(1);
            var replacement = new Thing(1);
            map.Add(old, 50, 50);
            Assert.Same(old, map.Add(replacement, 120, 120));

            Assert.False(map.Remove(old));
            Assert.False(map.Move(old, 51, 50));
            Assert.Empty(map.InView(50, 50));
            Assert.Equal(new[] { replacement }, map.InView(120, 120));
            Assert.Equal(1, map.Count);
        }

        [Theory]
        [InlineData(200, 0)]
        [InlineData(0, 200)]
        public void CellsOutsideTheMapAreRefused(ushort x, ushort y)
        {
            var map = Map();
            var thing = new Thing(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Add(thing, x, y));
            map.Add(thing, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Move(thing, x, y));
        }

        [Fact]
        public void ManyThreadsCanMoveAtOnce()
        {
            var map = Map(400, 400);
            var things = Enumerable.Range(1, 64).Select(i => new Thing((uint)i)).ToArray();
            foreach (var thing in things)
                map.Add(thing, 0, 0);

            Parallel.ForEach(things, thing =>
            {
                for (int step = 0; step < 300; step++)
                {
                    map.Move(thing, (ushort)(step + thing.Identity), (ushort)step);
                    map.InView((ushort)step, (ushort)step);
                }
            });

            Assert.Equal(64, map.Count);
            foreach (var thing in things)
            {
                Assert.True(map.TryGetPosition(thing, out var x, out var y));
                Assert.Equal((299 + thing.Identity, 299u), ((uint)x, (uint)y));
                Assert.Contains(thing, map.InView(x, y));
            }
        }
    }
}

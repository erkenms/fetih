namespace Fetih.Game.Tests.World
{
    using Fetih.Game.World;
    using Xunit;

    public class PortalTableTests
    {
        [Fact]
        public void TheShippedPortalFileLoads()
        {
            var table = new PortalTable(PortalTable.Load(DataFiles.PathOf("portals.json")));
            Assert.True(table.TryGet(1002, 3, out var stable));
            Assert.Equal(new PortalTarget(1006, 37, 31), stable);
        }

        [Fact]
        public void APortalListedTwiceIsRefused()
        {
            var path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, @"{ ""portals"": [
  { ""map"": 1002, ""portal"": 3, ""toMap"": 1006, ""toX"": 1, ""toY"": 1 },
  { ""map"": 1002, ""portal"": 3, ""toMap"": 1004, ""toX"": 2, ""toY"": 2 }
] }");
                var e = Assert.Throws<System.IO.InvalidDataException>(() => PortalTable.Load(path));
                Assert.Contains("1002/3", e.Message);
            }
            finally { System.IO.File.Delete(path); }
        }
    }
}

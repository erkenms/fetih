namespace Comet.Game.Tests.Npcs
{
    using System.Linq;
    using Comet.Game.Npcs;
    using Comet.Game.Tests.World;
    using Comet.Game.World;
    using Xunit;

    public class NpcDataTests
    {
        [Fact]
        public void EveryTalkerIsAPlacedNpcOnAKnownMap()
        {
            var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json")).ToDictionary(n => n.NpcID);
            var maps = MapTypes.Load(DataFiles.PathOf("maps.json")).Select(m => m.MapID).ToHashSet();
            var talkers = Talkers.Load(DataFiles.PathOf("talkers.json"));

            Assert.NotEmpty(talkers);
            foreach (var talker in talkers)
            {
                Assert.True(npcs.TryGetValue(talker.NpcID, out var npc),
                    $"Talker {talker.NpcID} is not in npcs.json");
                Assert.True(maps.Contains(npc.MapID),
                    $"Talker {talker.NpcID} stands on map {npc.MapID}, which is not in maps.json");
            }
        }

        [Fact]
        public void EveryTeleporterIsAPlacedNpcWithValidDestinationMaps()
        {
            var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json")).ToDictionary(n => n.NpcID);
            var maps = MapTypes.Load(DataFiles.PathOf("maps.json")).Select(m => m.MapID).ToHashSet();
            var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));

            Assert.NotEmpty(teleporters);
            foreach (var teleporter in teleporters)
            {
                Assert.True(npcs.ContainsKey(teleporter.NpcID),
                    $"Teleporter {teleporter.NpcID} is not in npcs.json");
                foreach (var destination in teleporter.Destinations)
                {
                    Assert.True(maps.Contains(destination.MapID),
                        $"Teleporter {teleporter.NpcID} destination '{destination.Name}' targets map {destination.MapID}, which is not in maps.json");
                }
            }
        }

        [RealClientFact]
        public void EveryTeleporterDestinationIsWalkableInTheRealClient()
        {
            var store = new MapStore(RealClientFactAttribute.Directory);
            var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));

            foreach (var teleporter in teleporters)
            {
                foreach (var destination in teleporter.Destinations)
                {
                    Assert.True(store.Exists(destination.MapID),
                        $"Teleporter {teleporter.NpcID} destination map {destination.MapID} is missing from GameMap.dat");
                    var grid = store.Get(destination.MapID);
                    Assert.True(grid.IsWalkable(destination.X, destination.Y),
                        $"Teleporter {teleporter.NpcID} destination ({destination.MapID}, {destination.X}, {destination.Y}) is not walkable");
                }
            }
        }
    }
}

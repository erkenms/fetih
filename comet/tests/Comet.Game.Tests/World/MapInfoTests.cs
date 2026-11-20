namespace Comet.Game.Tests.World
{
    using System;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using Xunit;

    public class MapInfoTests
    {
        [Fact]
        public void MapInfoHasMapDocThenFlags()
        {
            var bytes = new MsgMapInfo { MapID = 1036, DocID = 1036, Flags = MapFlags.BoothEnable }.Encode();
            Assert.Equal(20, bytes.Length);
            Assert.Equal(1110, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1036u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1036u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(0x10UL, BitConverter.ToUInt64(bytes, 12));
        }

        [Fact]
        public void TheShippedMapFileLoads()
        {
            var types = new MapTypes(MapTypes.Load(DataFiles.PathOf("maps.json")));
            Assert.True(types.Count > 100);
            Assert.Equal(MapFlags.ChangeMapDisable | MapFlags.RecordDisable | MapFlags.PkDisable |
                MapFlags.BoothEnable, types.Of(1036));
            Assert.Equal(MapFlags.PkDisable, types.Of(1002));
            Assert.Equal(MapFlags.None, types.Of(1010));
        }
    }
}

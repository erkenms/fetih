namespace Fetih.Game.Tests.World
{
    using System;
    using Fetih.Game.Packets;
    using Fetih.Game.World;
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

        [Fact]
        public void ShippedMapsHaveRebornPointsNamesAndScrolls()
        {
            var types = new MapTypes(MapTypes.Load(DataFiles.PathOf("maps.json")));
            Assert.Equal(new MapPoint(1000, 499, 650), types.RebornPointOf(1001));
            Assert.Equal(new MapPoint(1002, 430, 380), types.RebornPointOf(1002));
            Assert.Equal(new MapPoint(Fetih.Game.Pk.Jail.MapID, Fetih.Game.Pk.Jail.X, Fetih.Game.Pk.Jail.Y), types.RebornPointOf(Fetih.Game.Pk.Jail.MapID));
            Assert.Null(types.RebornPointOf(999_999));

            // Saved at the reborn point only on RecordDisable maps
            Assert.Equal(new MapPoint(1002, 430, 380), types.LoginPointOf(1006));
            Assert.Null(types.LoginPointOf(1001));

            Assert.Equal(new MapPoint(1015, 717, 577), types.ScrollPointOf(1060024));
            Assert.Null(types.ScrollPointOf(1000000));

            Assert.Equal(1002u, types.Find("twin_city").MapID);
            Assert.Equal(1002u, types.Find("TwinCity").MapID);
            Assert.Equal(1002u, types.Find("tc").MapID);
            Assert.Equal(1015u, types.Find("bird").MapID);
            Assert.Null(types.Find("lab"));
            Assert.Null(types.Find("nowhere"));
        }
    }
}

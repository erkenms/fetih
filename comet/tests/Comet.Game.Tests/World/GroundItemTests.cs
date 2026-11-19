namespace Comet.Game.Tests.World
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using Xunit;

    public class GroundItemTests
    {
        [Theory]
        [InlineData(1u, 1090000u)]
        [InlineData(50u, 1090000u)]
        [InlineData(51u, 1090010u)]
        [InlineData(101u, 1090020u)]
        [InlineData(1001u, 1091000u)]
        [InlineData(5001u, 1091010u)]
        [InlineData(10001u, 1091020u)]
        public void SilverPilesGrowWithTheAmount(uint amount, uint type) =>
            Assert.Equal(type, GroundItem.SilverType(amount));

        [Fact]
        public void MapItemLayout()
        {
            var item = new GroundItem { X = 430, Y = 380, TypeID = 410301, Item = new DbItem { Color = 5 } };
            var bytes = new MsgMapItem(item, MsgMapItem.MapItemAction.Create).Encode();
            Assert.Equal(24, bytes.Length);
            Assert.Equal(1101, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal((item.Identity, 410301u), (BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8)));
            Assert.Equal((430, 380, 5, 1), (BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 14),
                BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18)));

            var pick = new MsgMapItem();
            pick.Decode(Client.GroundItemMessage.BuildPick(77, 430, 381));
            Assert.Equal((77u, (ushort)430, (ushort)381, MsgMapItem.MapItemAction.Pick), (pick.ID, pick.X, pick.Y, pick.Action));
        }

        [Fact]
        public void GroundItemsGetTheirOwnIDs()
        {
            Assert.NotEqual(new GroundItem().Identity, new GroundItem().Identity);
        }
    }
}

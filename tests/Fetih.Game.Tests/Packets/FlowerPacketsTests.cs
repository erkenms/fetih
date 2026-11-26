namespace Fetih.Game.Tests.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Flowers;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Network.Packets;
    using Xunit;

    public class FlowerPacketsTests
    {
        [Fact]
        public void MsgFlowerDecodesClientSend()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgFlower);
            writer.Write(0u);           // mode 0
            writer.Write(1000042u);     // target
            writer.Write(555u);         // item ID
            writer.Write(new byte[44]); // pad to 60

            var msg = new MsgFlower();
            msg.Decode(writer.ToArray());

            Assert.Equal(MsgFlower.SendFlowerMode, msg.Mode);
            Assert.Equal(1000042u, msg.TargetID);
            Assert.Equal(555u, msg.ItemID);
        }

        [Fact]
        public void MsgFlowerSentEncodesAtClientOffsets()
        {
            var bytes = MsgFlower.Sent("Romeo", "Juliet", 99, FlowerKind.Orchid, isFree: true).Encode();
            Assert.Equal(60, bytes.Length);
            Assert.Equal((ushort)PacketType.MsgFlower, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal("Romeo", Encoding.ASCII.GetString(bytes, 16, 5));
            Assert.Equal("Juliet", Encoding.ASCII.GetString(bytes, 32, 6));
            Assert.Equal(99u, BitConverter.ToUInt32(bytes, 48));
            Assert.Equal((uint)FlowerKind.Orchid, BitConverter.ToUInt32(bytes, 52));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 56));
        }

        [Fact]
        public void MsgFlowerInfoEncodesTotalAndTodayPairs()
        {
            var today = new DateTime(2026, 11, 24);
            var record = new FlowerRecord(1000002, "Juliet", 100, 10, 200, 20, 300, 30, 400, 40, today);
            var bytes = MsgFlower.Info(1000002, record, today).Encode();

            Assert.Equal(60, bytes.Length);
            Assert.Equal(MsgFlower.InfoMode, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal(100u, BitConverter.ToUInt32(bytes, 16)); // RedRoses total
            Assert.Equal(10u, BitConverter.ToUInt32(bytes, 20));  // RedRoses today
            Assert.Equal(200u, BitConverter.ToUInt32(bytes, 24)); // Lilies total
            Assert.Equal(20u, BitConverter.ToUInt32(bytes, 28));  // Lilies today
            Assert.Equal(300u, BitConverter.ToUInt32(bytes, 32)); // Orchids total
            Assert.Equal(30u, BitConverter.ToUInt32(bytes, 36));  // Orchids today
            Assert.Equal(400u, BitConverter.ToUInt32(bytes, 40)); // Tulips total
            Assert.Equal(40u, BitConverter.ToUInt32(bytes, 44));  // Tulips today
        }

        [Fact]
        public void MsgRankDecodesPageQuery()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgRank);
            writer.Write(MsgRank.QueryMode);
            writer.Write(new byte[3]);
            writer.Write(FlowerBoard.LilyRankType);
            writer.Write((ushort)0);
            writer.Write((byte)2);
            writer.Write(new byte[65]);

            var msg = new MsgRank();
            msg.Decode(writer.ToArray());

            Assert.Equal(MsgRank.QueryMode, msg.Mode);
            Assert.Equal(FlowerBoard.LilyRankType, msg.Data);
            Assert.Equal(2, msg.Page);
        }

        [Fact]
        public void MsgRankPageResultEncodes56ByteEntries()
        {
            var entries = new List<FlowerEntry>
            {
                new FlowerEntry(1000001, "Alice", 999, 1),
                new FlowerEntry(1000002, "Bella", 500, 2),
            };
            var bytes = MsgRank.PageResult(FlowerBoard.RedRoseRankType, 0, entries).Encode();

            Assert.Equal(24 + 56 * 2, bytes.Length);
            Assert.Equal(MsgRank.QueryMode, bytes[4]);
            Assert.Equal(FlowerBoard.RedRoseRankType, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(2, BitConverter.ToInt32(bytes, 16));

            // Entry 0 at 24
            Assert.Equal(1ul, BitConverter.ToUInt64(bytes, 24));
            Assert.Equal(999ul, BitConverter.ToUInt64(bytes, 32));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 40));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 44));
            Assert.Equal("Alice", Encoding.ASCII.GetString(bytes, 64, 5));

            // Entry 1 at 80
            Assert.Equal(2ul, BitConverter.ToUInt64(bytes, 80));
            Assert.Equal(500ul, BitConverter.ToUInt64(bytes, 88));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 100));
            Assert.Equal("Bella", Encoding.ASCII.GetString(bytes, 120, 5));
        }

        [Fact]
        public void MsgRankOwnRankAndEndEncode80Bytes()
        {
            var own = MsgRank.OwnRank(FlowerBoard.OrchidRankType, 3, 150, 1000009, "Chloe").Encode();
            Assert.Equal(80, own.Length);
            Assert.Equal(MsgRank.OwnFlowerRankMode, own[4]);
            Assert.Equal(FlowerBoard.OrchidRankType, BitConverter.ToUInt32(own, 8));
            Assert.Equal(1, BitConverter.ToInt32(own, 16));
            Assert.Equal(3, own[24]);
            Assert.Equal(1000009u, BitConverter.ToUInt32(own, 44));

            var end = MsgRank.OwnRankEnd().Encode();
            Assert.Equal(80, end.Length);
            Assert.Equal(MsgRank.OwnRankEndMode, end[4]);
        }

        [Fact]
        public void MsgPlayerWritesFlowerCharmAt123()
        {
            var record = new DbCharacter(7, 1000042, "Juliet") { Mesh = 2001 };
            var msg = new MsgPlayer(new Character(record)) { FlowerCharm = 30010002u };
            var bytes = msg.Encode();
            Assert.Equal(30010002u, BitConverter.ToUInt32(bytes, 123));
        }

        [Fact]
        public void PacketHandlersRegisterFlowerAndRank()
        {
            Assert.True(PacketHandlers.Handles(PacketType.MsgFlower));
            Assert.True(PacketHandlers.Handles(PacketType.MsgRank));
        }
    }
}

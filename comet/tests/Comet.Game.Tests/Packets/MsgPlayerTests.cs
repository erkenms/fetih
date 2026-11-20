namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class MsgPlayerTests
    {
        private static byte[] Encode()
        {
            var record = new DbCharacter(7, 1000042, "Hero");
            record.Mesh = 1004;
            record.Avatar = 12;
            record.Hairstyle = 410;
            record.Level = 70;
            record.CurrentClass = 15;
            record.Rebirths = 1;
            record.MapID = 1002;
            record.X = 438;
            record.Y = 377;
            var character = new Character(record) { Direction = 5 };
            return new MsgPlayer(character).Encode();
        }

        [Fact]
        public void FieldsAreAtClientOffsets()
        {
            var bytes = Encode();
            Assert.Equal(10014, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(121004u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000042u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(438, BitConverter.ToUInt16(bytes, 92));
            Assert.Equal(377, BitConverter.ToUInt16(bytes, 94));
            Assert.Equal(410, BitConverter.ToUInt16(bytes, 96));
            Assert.Equal(5, bytes[98]);
            Assert.Equal(1, bytes[106]);
            Assert.Equal(70, BitConverter.ToUInt16(bytes, 107));
            Assert.Equal(15, BitConverter.ToUInt16(bytes, 219));
        }

        [Fact]
        public void StatusFlagsAreAt22()
        {
            var record = new DbCharacter(7, 1000042, "Hero") { Mesh = 1004 };
            var bytes = new MsgPlayer(new Character(record)) { Status = 0x8001 }.Encode();
            Assert.Equal(0x8001UL, BitConverter.ToUInt64(bytes, 22));
            Assert.Equal(0, BitConverter.ToUInt16(bytes, 42));
        }

        [Fact]
        public void PositionFlagIsZero()
        {
            // The client only places the player at X, Y when the byte at 225 is 0
            Assert.Equal(0, Encode()[225]);
        }

        [Fact]
        public void StringsStartAtOffset226()
        {
            var bytes = Encode();
            Assert.Equal(3, bytes[226]);
            Assert.Equal(4, bytes[227]);
            Assert.Equal("Hero", Encoding.ASCII.GetString(bytes, 228, 4));
            Assert.Equal(0, bytes[232]);
            Assert.Equal(0, bytes[233]);
            Assert.Equal(234, bytes.Length);
        }

        [Fact]
        public void RemoveEntityIsAction135WithTheId()
        {
            var bytes = MsgAction.RemoveEntity(1000042).Encode();
            Assert.Equal(38, bytes.Length);
            Assert.Equal(10010, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000042u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(135, BitConverter.ToUInt16(bytes, 20));
        }
    }
}

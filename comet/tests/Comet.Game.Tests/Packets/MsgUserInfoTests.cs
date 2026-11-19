namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Xunit;

    public class MsgUserInfoTests
    {
        private static byte[] Encode()
        {
            var character = new DbCharacter(7, 1000042, "Hero");
            character.Mesh = 1003;
            character.Avatar = 12;
            character.Hairstyle = 410;
            character.Silver = 1000;
            character.Jewels = 25;
            character.Experience = 0x0102030405060708;
            character.Strength = 4;
            character.Agility = 6;
            character.Vitality = 12;
            character.Spirit = 1;
            character.AttributePoints = 3;
            character.HealthPoints = 318;
            character.ManaPoints = 5;
            character.KillPoints = 2;
            character.Level = 15;
            character.CurrentClass = 10;
            character.Rebirths = 1;
            character.QuizPoints = 77;
            character.EnlightenPoints = 9;
            return new MsgUserInfo(character).Encode();
        }

        [Fact]
        public void FieldsAreAtClientOffsets()
        {
            var bytes = Encode();
            Assert.Equal(1006, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000042u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(121003u, BitConverter.ToUInt32(bytes, 10));
            Assert.Equal(410, BitConverter.ToUInt16(bytes, 14));
            Assert.Equal(1000u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(25u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(0x0102030405060708ul, BitConverter.ToUInt64(bytes, 24));
            Assert.Equal(4, BitConverter.ToUInt16(bytes, 52));
            Assert.Equal(6, BitConverter.ToUInt16(bytes, 54));
            Assert.Equal(12, BitConverter.ToUInt16(bytes, 56));
            Assert.Equal(1, BitConverter.ToUInt16(bytes, 58));
            Assert.Equal(3, BitConverter.ToUInt16(bytes, 60));
            Assert.Equal(318, BitConverter.ToUInt16(bytes, 62));
            Assert.Equal(5, BitConverter.ToUInt16(bytes, 64));
            Assert.Equal(2, BitConverter.ToUInt16(bytes, 66));
            Assert.Equal(15, bytes[68]);
            Assert.Equal(10, bytes[69]);
            Assert.Equal(1, bytes[73]);
            Assert.Equal(77u, BitConverter.ToUInt32(bytes, 75));
            Assert.Equal(9, BitConverter.ToUInt16(bytes, 79));
        }

        [Fact]
        public void StringsStartAtOffset110()
        {
            var bytes = Encode();
            Assert.Equal(3, bytes[110]);
            Assert.Equal(4, bytes[111]);
            Assert.Equal("Hero", Encoding.ASCII.GetString(bytes, 112, 4));
            Assert.Equal(0, bytes[116]);
            Assert.Equal(4, bytes[117]);
            Assert.Equal("None", Encoding.ASCII.GetString(bytes, 118, 4));
            Assert.Equal(122, bytes.Length);
        }
    }
}

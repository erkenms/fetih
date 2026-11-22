namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
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
        public void PlayersShowWhatTheyWear()
        {
            var character = new Character(new DbCharacter(1, 1000001, "Hero") { Mesh = 1003, Level = 1 });
            var equipment = new Equipment(new[]
                {
                    new DbItem { ItemID = 1, TypeID = 111003, Position = 1, Color = 5 },
                    new DbItem { ItemID = 2, TypeID = 130003, Position = 3, Color = 3 },
                    new DbItem { ItemID = 3, TypeID = 410301, Position = 4 },
                    new DbItem { ItemID = 4, TypeID = 900000, Position = 5, Color = 7 },
                    new DbItem { ItemID = 5, TypeID = 181305, Position = 9 },
                });
            var bytes = new MsgPlayer(character, equipment).Encode();
            Assert.Equal((111003u, 181305u, 130003u), (BitConverter.ToUInt32(bytes, 44),
                BitConverter.ToUInt32(bytes, 48), BitConverter.ToUInt32(bytes, 52)));
            Assert.Equal((410301u, 900000u), (BitConverter.ToUInt32(bytes, 56), BitConverter.ToUInt32(bytes, 60)));
            Assert.Equal((3, 7, 5), (BitConverter.ToUInt16(bytes, 131), BitConverter.ToUInt16(bytes, 133),
                BitConverter.ToUInt16(bytes, 135)));
        }

        [Fact]
        public void TheSteedIsShownToOthers()
        {
            var steed = new DbItem { ItemID = 9, TypeID = 300000, Plus = 6, Position = (byte)ItemPosition.Steed };
            var bytes = new MsgPlayer(new Character(new DbCharacter(7, 1000042, "Hero")), new Equipment(new[] { steed }))
                .Encode();
            Assert.Equal(300000u, BitConverter.ToUInt32(bytes, 72));
            Assert.Equal(6, bytes[141]);
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 76));
        }

        [Fact]
        public void MonstersAreSentAsPlayersWithHealthAndLevel()
        {
            var type = new MonsterType { ID = 1, Name = "Pheasant", Life = 33, Lookface = 104, Level = 3 };
            var generator = new Generator { ID = 1, MapID = 1002, X = 10, Y = 10, Amount = 1, MonsterID = 1 };
            var monster = new Monster(Monster.FirstID, type, generator, null) { X = 11, Y = 12, Direction = 3 };
            monster.Revive();
            var bytes = new MsgPlayer(monster).Encode();
            Assert.Equal(104u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(Monster.FirstID, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(33u, BitConverter.ToUInt32(bytes, 84));
            Assert.Equal(3, BitConverter.ToUInt16(bytes, 90));
            Assert.Equal((11, 12), (BitConverter.ToUInt16(bytes, 92), BitConverter.ToUInt16(bytes, 94)));
            Assert.Equal(3, bytes[98]);
            Assert.Equal("Pheasant", Encoding.ASCII.GetString(bytes, 228, 8));
        }

        [Fact]
        public void PointsAndPastClassesAreShown()
        {
            var record = new DbCharacter(7, 1000042, "Hero")
            {
                QuizPoints = 1234, EnlightenPoints = 300, AncestorClass = 25, PreviousClass = 135, CurrentClass = 41,
            };
            var bytes = new MsgPlayer(new Character(record)).Encode();
            Assert.Equal(1234u, BitConverter.ToUInt32(bytes, 137));
            Assert.Equal(300, BitConverter.ToUInt16(bytes, 151));
            Assert.Equal((25, 135, 41), (BitConverter.ToUInt16(bytes, 215), BitConverter.ToUInt16(bytes, 217),
                BitConverter.ToUInt16(bytes, 219)));
        }

        [Fact]
        public void BossesAreMarked()
        {
            var type = new MonsterType { ID = 1, Name = "Boss", Life = 1, Lookface = 104, Level = 3, Boss = true };
            var generator = new Generator { ID = 1, MapID = 1002, X = 10, Y = 10, Amount = 1, MonsterID = 1 };
            var monster = new Monster(Monster.FirstID, type, generator, null);
            Assert.Equal(1, new MsgPlayer(monster).Encode()[189]);
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

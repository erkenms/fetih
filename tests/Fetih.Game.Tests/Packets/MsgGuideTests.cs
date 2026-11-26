namespace Fetih.Game.Tests.Packets
{
    using System;
    using System.Collections.Generic;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Packets;
    using Fetih.Game.Social;
    using Fetih.Network.Packets;
    using Xunit;
    using static Fetih.Game.Packets.MsgContribute;
    using static Fetih.Game.Packets.MsgGuide;
    using static Fetih.Game.Packets.MsgGuideInfo;

    public sealed class MsgGuideTests
    {
        [Fact]
        public void MsgGuideDecodesReal5619ClientPacket()
        {
            // Captured from the 5619 client: RequestMentor (2) from 1000001 to 1000000, value 1
            byte[] raw = Convert.FromHexString("180011080200000041420F0040420F000100000000000000");
            var msg = new MsgGuide();
            msg.Decode(raw);

            Assert.Equal(24, msg.Length);
            Assert.Equal(PacketType.MsgGuide, msg.Type);
            Assert.Equal(GuideAction.RequestMentor, msg.Action);
            Assert.Equal(1000001u, msg.Identity);
            Assert.Equal(1000000u, msg.Param);
            Assert.Equal(1u, msg.Value);
            Assert.Equal(string.Empty, msg.Name);
        }

        [Fact]
        public void MsgGuideRoundTripsEmptyAndNamed()
        {
            var empty = new MsgGuide
            {
                Action = GuideAction.ClearBattlePower,
                Identity = 1000001,
            };
            byte[] emptyBytes = empty.Encode();
            Assert.Equal(MsgGuide.MinSize, emptyBytes.Length);
            Assert.Equal((ushort)PacketType.MsgGuide, BitConverter.ToUInt16(emptyBytes, 2));

            var named = new MsgGuide
            {
                Action = GuideAction.AcceptInviteApprentice,
                Identity = 1000000,
                Param = 1000001,
                Value = 1,
                Name = "Mentor",
            };
            byte[] bytes = named.Encode();
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)bytes.Length);
            var decoded = new MsgGuide();
            decoded.Decode(bytes);

            Assert.Equal(GuideAction.AcceptInviteApprentice, decoded.Action);
            Assert.Equal(1000000u, decoded.Identity);
            Assert.Equal(1000001u, decoded.Param);
            Assert.Equal(1u, decoded.Value);
            Assert.Equal("Mentor", decoded.Name);
        }

        [Fact]
        public void MsgGuideInfoDecodesReal5619ClientQuery()
        {
            // Captured from the 5619 client: query Mentor (1), 80 bytes
            byte[] raw = new byte[80];
            raw[0] = 0x50;
            raw[2] = 0x12;
            raw[3] = 0x08;
            raw[4] = 0x01;
            var msg = new MsgGuideInfo();
            msg.Decode(raw);

            Assert.Equal(80, msg.Length);
            Assert.Equal(PacketType.MsgGuideInfo, msg.Type);
            Assert.Equal(GuideInfoMode.Mentor, msg.Mode);
            Assert.Equal(0u, msg.Identity);
        }

        [Fact]
        public void MsgGuideInfoEncodesExactOffsetsAndStrings()
        {
            var info = new MsgGuideInfo
            {
                Mode = GuideInfoMode.Apprentice,
                Identity = 1000000,
                TargetID = 1000001,
                Mesh = 10003,
                SharedBattlePower = 15,
                BetrayHour = MsgGuideInfo.NotBetraying,
                EnrolDate = 20261124,
                Level = 45,
                Profession = 10,
                PkPoints = 12,
                SyndicateID = 7,
                SyndicateRank = 1000,
                SyndicateTitle = 2,
                Sex = 1,
                Online = true,
                Experience = 1200,
                Blessing = 7,
                Composition = 150,
                Strings = new List<string> { "Master", "Pupil", "None" },
            };
            byte[] bytes = info.Encode();
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)bytes.Length);

            Assert.Equal((ushort)PacketType.MsgGuideInfo, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000000u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(10003u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(15u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(999999u, BitConverter.ToUInt32(bytes, 24));
            Assert.Equal(20261124u, BitConverter.ToUInt32(bytes, 28));
            Assert.Equal((byte)45, bytes[32]);
            Assert.Equal((byte)10, bytes[33]);
            Assert.Equal((short)12, BitConverter.ToInt16(bytes, 34));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 36));
            Assert.Equal(1000u, BitConverter.ToUInt32(bytes, 40));
            Assert.Equal((ushort)2, BitConverter.ToUInt16(bytes, 44));
            Assert.Equal(1, BitConverter.ToInt32(bytes, 52));
            Assert.Equal((byte)1, bytes[56]);
            Assert.Equal(1200UL, BitConverter.ToUInt64(bytes, 64));
            Assert.Equal((ushort)7, BitConverter.ToUInt16(bytes, 72));
            Assert.Equal((ushort)150, BitConverter.ToUInt16(bytes, 74));

            var decoded = new MsgGuideInfo();
            decoded.Decode(bytes);
            Assert.Equal(new[] { "Master", "Pupil", "None" }, decoded.Strings);
        }

        [Fact]
        public void MsgContributeDecodesReal5619ClientQueryAndRoundTrips()
        {
            // Captured from the 5619 client: QueryRewards (5), 40 bytes
            byte[] raw = Convert.FromHexString(
                "28001308050000000000000000000000000000000000000000000000000000000000000000000000");
            var query = new MsgContribute();
            query.Decode(raw);
            Assert.Equal(40, query.Length);
            Assert.Equal(PacketType.MsgContribute, query.Type);
            Assert.Equal(ContributeAction.Rewards, query.Action);

            var entry = new MsgContribute
            {
                Action = ContributeAction.Apprentices,
                Generation = 1,
                Experience = 1800,
                Blessing = 16,
                Composition = 200,
                Strings = new List<string> { "Master", "Pupil" },
            };
            byte[] bytes = entry.Encode();
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)bytes.Length);
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(1800UL, BitConverter.ToUInt64(bytes, 24));
            Assert.Equal((ushort)16, BitConverter.ToUInt16(bytes, 32));
            Assert.Equal((ushort)200, BitConverter.ToUInt16(bytes, 34));

            var decoded = new MsgContribute();
            decoded.Decode(bytes);
            Assert.Equal(ContributeAction.Apprentices, decoded.Action);
            Assert.Equal(1u, decoded.Generation);
            Assert.Equal(1800UL, decoded.Experience);
            Assert.Equal((ushort)16, decoded.Blessing);
            Assert.Equal((ushort)200, decoded.Composition);
            Assert.Equal(new[] { "Master", "Pupil" }, decoded.Strings);
        }

        [Fact]
        public void TutorTypesParsesIniAndChecksQualifications()
        {
            var types = TutorTypes.Parse("70,109,2,10\r\n110,119,3,15\r\n120,129,5,25\r\n130,133,7,35\r\n134,140,10,50\r\n");
            Assert.Equal(5, types.Count);
            Assert.Equal(70, types.MinMentorLevel);

            static DbCharacter Char(byte level, byte rebirths = 0, byte firstLife = 0) =>
                new DbCharacter(1, "Test") { Level = level, Rebirths = rebirths, FirstLifeLevel = firstLife };

            var mentor70 = Char(70);
            var lowMentor = Char(69);
            var rebornMentor = Char(15, rebirths: 1, firstLife: 120);
            var app40 = Char(40);
            var app41 = Char(41);
            var rebornApp = Char(100, rebirths: 1);

            Assert.True(types.CanBeMentor(mentor70));
            Assert.False(types.CanBeMentor(lowMentor));
            Assert.True(types.CanBeMentor(rebornMentor));

            Assert.Equal(2u, types.MaxApprentices(mentor70));
            Assert.Equal(10u, types.MaxBattlePower(mentor70));
            Assert.Equal(5u, types.MaxApprentices(rebornMentor));
            Assert.Equal(25u, types.MaxBattlePower(rebornMentor));

            // 0-rebirth gap is 30
            Assert.True(types.MeetsLevelGap(mentor70, app40));
            Assert.False(types.MeetsLevelGap(mentor70, app41));
            // Higher rebirth qualifies regardless of current level
            Assert.True(types.MeetsLevelGap(rebornMentor, app41));
            // Lower rebirth cannot mentor higher rebirth
            Assert.False(types.MeetsLevelGap(mentor70, rebornApp));
            // 1-rebirth gap is 20
            Assert.True(types.MeetsLevelGap(Char(120, rebirths: 1), rebornApp));
            Assert.False(types.MeetsLevelGap(Char(119, rebirths: 1), rebornApp));
            // 2-rebirth gap is 10
            Assert.True(types.MeetsLevelGap(Char(110, rebirths: 2), Char(100, rebirths: 2)));
            Assert.False(types.MeetsLevelGap(Char(109, rebirths: 2), Char(100, rebirths: 2)));
        }
    }
}

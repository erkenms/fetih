namespace Comet.Game.Tests.Packets
{
    using System;
    using Comet.Game.Combat;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Network.Packets;
    using Xunit;

    public class MsgSubProTests
    {
        [Fact]
        public void DecodesRealClientInfoRequest()
        {
            byte[] raw = Convert.FromHexString("1D00100906000000000000000000000000000000000000000000000000");
            var msg = new MsgSubPro();
            msg.Decode(raw);

            Assert.Equal(29, msg.Length);
            Assert.Equal(PacketType.MsgSubPro, msg.Type);
            Assert.Equal(MsgSubPro.SubProAction.RequestInfo, msg.Action);
            Assert.Equal(SubClassId.None, msg.SubClass);
        }

        [Theory]
        [InlineData(MsgSubPro.SubProAction.RequestSwitch, SubClassId.MartialArtist)]
        [InlineData(MsgSubPro.SubProAction.RequestSwitch, SubClassId.None)]
        [InlineData(MsgSubPro.SubProAction.RequestUpgrade, SubClassId.Warlock)]
        [InlineData(MsgSubPro.SubProAction.RequestLearn, SubClassId.ChiMaster)]
        [InlineData(MsgSubPro.SubProAction.RequestPromote, SubClassId.Wrangler)]
        public void DecodesClientActionPackets(MsgSubPro.SubProAction action, SubClassId subClass)
        {
            var raw = new byte[29];
            BitConverter.TryWriteBytes(raw.AsSpan(0), (ushort)29);
            BitConverter.TryWriteBytes(raw.AsSpan(2), (ushort)PacketType.MsgSubPro);
            BitConverter.TryWriteBytes(raw.AsSpan(4), (ushort)action);
            raw[6] = (byte)subClass;

            var msg = new MsgSubPro();
            msg.Decode(raw);

            Assert.Equal(action, msg.Action);
            Assert.Equal(subClass, msg.SubClass);
        }

        [Fact]
        public void EncodesSwitchUpgradeLearnAndPromote()
        {
            var sw = MsgSubPro.Switch(SubClassId.Performer).Encode();
            Assert.Equal(29, sw.Length);
            Assert.Equal((ushort)PacketType.MsgSubPro, BitConverter.ToUInt16(sw, 2));
            Assert.Equal((ushort)MsgSubPro.SubProAction.Switch, BitConverter.ToUInt16(sw, 4));
            Assert.Equal((byte)SubClassId.Performer, sw[6]);

            var up = MsgSubPro.Upgrade(SubClassId.Sage).Encode();
            Assert.Equal(29, up.Length);
            Assert.Equal((ushort)MsgSubPro.SubProAction.Upgrade, BitConverter.ToUInt16(up, 4));
            Assert.Equal((byte)SubClassId.Sage, up[6]);

            var learn = MsgSubPro.Learn(SubClassId.MartialArtist, 1).Encode();
            Assert.Equal(29, learn.Length);
            Assert.Equal((ushort)MsgSubPro.SubProAction.Learn, BitConverter.ToUInt16(learn, 4));
            Assert.Equal((byte)SubClassId.MartialArtist, learn[6]);
            Assert.Equal(1, learn[7]);

            var promote = MsgSubPro.Promote(SubClassId.Wrangler, 4).Encode();
            Assert.Equal(29, promote.Length);
            Assert.Equal((ushort)MsgSubPro.SubProAction.Promote, BitConverter.ToUInt16(promote, 4));
            Assert.Equal((byte)SubClassId.Wrangler, promote[6]);
            Assert.Equal(4, promote[7]);
        }

        [Fact]
        public void EncodesShowListAndStudyPoints()
        {
            var show = MsgSubPro.Show(12345, new[]
            {
                (SubClassId.MartialArtist, (byte)2, (byte)3),
                (SubClassId.Wrangler, (byte)1, (byte)1),
            }).Encode();
            Assert.Equal(26 + 2 * 3, show.Length);
            Assert.Equal((ushort)MsgSubPro.SubProAction.Show, BitConverter.ToUInt16(show, 4));
            Assert.Equal(12345UL, BitConverter.ToUInt64(show, 6));
            Assert.Equal(2u, BitConverter.ToUInt32(show, 22));
            Assert.Equal((byte)SubClassId.MartialArtist, show[26]);
            Assert.Equal(2, show[27]);
            Assert.Equal(3, show[28]);
            Assert.Equal((byte)SubClassId.Wrangler, show[29]);
            Assert.Equal(1, show[30]);
            Assert.Equal(1, show[31]);

            var study = MsgSubPro.StudyPointsPacket(500, 50).Encode();
            Assert.Equal(29, study.Length);
            Assert.Equal((ushort)MsgSubPro.SubProAction.StudyPoints, BitConverter.ToUInt16(study, 4));
            Assert.Equal(500UL, BitConverter.ToUInt64(study, 6));
            Assert.Equal(50UL, BitConverter.ToUInt64(study, 14));
        }

        [Fact]
        public void EncodesSubClassFieldsInUserInfoPlayerAndAttribInfo()
        {
            var db = new DbCharacter(1, 1000001, "Tester") { ActiveSubClass = (byte)SubClassId.Performer };
            ulong mask = 500_003UL;

            var userInfo = new MsgUserInfo(db, mask).Encode();
            Assert.Equal((byte)SubClassId.Performer, userInfo[97]);
            Assert.Equal(mask, BitConverter.ToUInt64(userInfo, 98));

            var player = new MsgPlayer(new Character(db))
            {
                ActiveSubClass = (byte)SubClassId.Performer,
                AllSubProf = mask,
            }.Encode();
            Assert.Equal((byte)SubClassId.Performer, player[206]);
            Assert.Equal(mask, BitConverter.ToUInt64(player, 207));

            var stats = new Stats(100, 120, 50, 10, 1, 1000, false)
            {
                CriticalStrike = 6,
                SkillCriticalStrike = 4,
                Immunity = 3,
                Penetration = 8,
                Detoxication = 24,
            };
            var attrib = new MsgPlayerAttribInfo(1000001, 500, 100, 10, stats).Encode();
            Assert.Equal(MsgPlayerAttribInfo.Size, attrib.Length);
            Assert.Equal(600u, BitConverter.ToUInt32(attrib, 68));
            Assert.Equal(400u, BitConverter.ToUInt32(attrib, 72));
            Assert.Equal(300u, BitConverter.ToUInt32(attrib, 76));
            Assert.Equal(800u, BitConverter.ToUInt32(attrib, 80));
            Assert.Equal(24u, BitConverter.ToUInt32(attrib, 96));
        }
    }
}

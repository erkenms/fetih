namespace Comet.Game.Tests.Tournaments
{
    using System;
    using System.Linq;
    using System.Text;
    using Comet.Game.Items;
    using Comet.Game.Nobility;
    using Comet.Game.Packets;
    using Comet.Game.Tournaments;
    using Comet.Network.Packets;
    using Xunit;

    public class TeamPkTests
    {
        [Theory]
        [InlineData(0, 1, 720794u)]   // Refined Team PK Champion Pack
        [InlineData(3, 1, 720797u)]   // Super Team PK Champion Pack
        [InlineData(1, 2, 720799u)]   // Unique Team PK 2nd Place Pack
        [InlineData(2, 3, 720804u)]   // Elite Team PK 3rd Place Pack
        [InlineData(3, 4, 720809u)]   // Super Team PK Top 8 Pack
        public void TeamPkPacksGoByGroupAndPlace(int group, int rank, uint pack) =>
            Assert.Equal(pack, TeamPk.Team.PackOf(group, rank));

        [Theory]
        [InlineData(0, 1, 721300u)]   // Perfect Pack, under 100
        [InlineData(3, 1, 721303u)]   // Perfect Pack, 130+
        [InlineData(1, 4, 721313u)]   // Delicate Pack, 100-119
        public void SkillTeamPkPacksGoByGroupAndPlace(int group, int rank, uint pack) =>
            Assert.Equal(pack, TeamPk.Skill.PackOf(group, rank));

        [Fact]
        public void EachEventHasItsManagerAndMatchPack()
        {
            Assert.Same(TeamPk.Team, TeamPk.ByManager(8158));
            Assert.Same(TeamPk.Skill, TeamPk.ByManager(8592));
            Assert.Null(TeamPk.ByManager(7879));
            Assert.Equal(720793u, TeamPk.Team.ExpPackType);
            Assert.Equal(720981u, TeamPk.Skill.ExpPackType);
            Assert.False(TeamPk.Team.Capped);
            Assert.True(TeamPk.Skill.Capped);
        }

        [Fact]
        public void OnTimeTheScoreDecidesThenWhoStillStands()
        {
            Assert.True(TeamPk.Compare(500, 1, 400, 5) > 0);
            Assert.True(TeamPk.Compare(400, 1, 400, 2) < 0);
            Assert.Equal(0, TeamPk.Compare(400, 2, 400, 2));
        }

        [Fact]
        public void SkillCapsLowerGearToLevel120AndPlusFourAndNobilityToEarl()
        {
            var types = new ItemTypeTable(new (uint ID, byte Level)[]
                {
                    (410289, 115), (410299, 120), (410309, 125), (410319, 130), (111003, 15),
                }.Select(t => new ItemType { ID = t.ID, RequiredLevel = t.Level }));
            Assert.Equal(410299u, SkillCaps.TypeOf(410319, types));
            Assert.Equal(410299u, SkillCaps.TypeOf(410309, types));
            Assert.Equal(410289u, SkillCaps.TypeOf(410289, types));
            Assert.Equal(111003u, SkillCaps.TypeOf(111003, types));
            Assert.Equal(1088000u, SkillCaps.TypeOf(1088000, types));

            Assert.Equal((byte)4, SkillCaps.PlusOf(12));
            Assert.Equal((byte)3, SkillCaps.PlusOf(3));
            Assert.Equal(NobilityRank.Earl, SkillCaps.RankOf(NobilityRank.King));
            Assert.Equal(NobilityRank.Baron, SkillCaps.RankOf(NobilityRank.Baron));
        }

        [Fact]
        public void TheArenicPacketCarriesActionResultAndSeconds()
        {
            var bytes = new MsgTeamPKArenic(PacketType.MsgTeamPopPKArenic, MsgTeamPKArenic.ArenicAction.Start)
            {
                Won = true, Seconds = 600,
            }.Encode();
            Assert.Equal(MsgTeamPKArenic.Size, bytes.Length);
            Assert.Equal(2250, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(600, BitConverter.ToUInt16(bytes, 44));
        }

        [Fact]
        public void TheScorePacketHoldsTwoTeams()
        {
            var bytes = new MsgTeamPKArenicScore(PacketType.MsgTeamPKArenicScore)
            {
                TeamA = 1000001, NameA = "Team of Alice", ScoreA = 1234,
                TeamB = 1000002, NameB = "Team of Bob", ScoreB = 99,
            }.Encode();
            Assert.Equal(MsgTeamPKArenicScore.Size, bytes.Length);
            Assert.Equal(2231, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal("Team of Alice", Encoding.ASCII.GetString(bytes, 28, 13));
            Assert.Equal(1234u, BitConverter.ToUInt32(bytes, 60));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 64));
            Assert.Equal(1000002u, BitConverter.ToUInt32(bytes, 68));
            Assert.Equal("Team of Bob", Encoding.ASCII.GetString(bytes, 88, 11));
            Assert.Equal(99u, BitConverter.ToUInt32(bytes, 120));
        }
    }
}

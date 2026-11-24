namespace Comet.Game.Tests.Social
{
    using System;
    using System.Collections.Generic;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Comet.Network.Packets;
    using Xunit;

    public class GuildArsenalTests
    {
        [Fact]
        public void ParseTotemPoleIniMatchesDefaults()
        {
            const string sample = """
                [Name]
                0=Headgear
                [Quality]
                fine=1000
                masterwork=16660
                [Add]
                1=90
                9=1000000
                12=1212340
                [Hole]
                1=33330
                2=133330
                """;
            var config = ArsenalConfig.Parse(sample);
            Assert.Equal(1_000u, config.EliteQuality);
            Assert.Equal(16_660u, config.SuperQuality);
            Assert.Equal(90u, config.PlusValues[1]);
            Assert.Equal(1_000_000u, config.PlusValues[9]);
            Assert.Equal(1_212_340u, config.PlusValues[12]);
            Assert.Equal(33_330u, config.OneSocket);
            Assert.Equal(133_330u, config.TwoSockets);
        }

        [Fact]
        public void DonationOfCalculatesQualityPlusAndSockets()
        {
            var config = ArsenalConfig.Default;
            // Unique (7) cannot be inscribed
            Assert.Equal(0u, config.DonationOf(410007, 12, 13, 13));
            // Elite (8) +0, 0 sockets
            Assert.Equal(1_000u, config.DonationOf(410008, 0, 0, 0));
            // Super (9) +12, 2 sockets = 16660 + 1212340 + 133330 = 1,362,330
            Assert.Equal(1_362_330u, config.DonationOf(410009, 12, 255, 255));
            // Elite (8) +5, 1 socket = 1000 + 12340 + 33330 = 46,670
            Assert.Equal(46_670u, config.DonationOf(111008, 5, 13, 0));
        }

        [Theory]
        [InlineData(111009u, TotemPoleType.Headgear)]
        [InlineData(130009u, TotemPoleType.Armor)]
        [InlineData(410009u, TotemPoleType.Weapon)]
        [InlineData(500009u, TotemPoleType.Weapon)]
        [InlineData(900009u, TotemPoleType.Weapon)]
        [InlineData(150009u, TotemPoleType.Ring)]
        [InlineData(160009u, TotemPoleType.Boots)]
        [InlineData(120009u, TotemPoleType.Necklace)]
        [InlineData(201009u, TotemPoleType.Fan)]
        [InlineData(202009u, TotemPoleType.Tower)]
        [InlineData(1050009u, null)]
        [InlineData(181009u, null)]
        [InlineData(300009u, null)]
        public void PoleTypeMapsEquipmentSlots(uint typeID, TotemPoleType? expected) =>
            Assert.Equal(expected, ArsenalConfig.PoleTypeOf(typeID));

        [Fact]
        public void UnlockAndEnhanceCostsMatchClientTables()
        {
            Assert.Equal(5_000_000ul, ArsenalConfig.UnlockCost(1));
            Assert.Equal(5_000_000ul, ArsenalConfig.UnlockCost(2));
            Assert.Equal(10_000_000ul, ArsenalConfig.UnlockCost(3));
            Assert.Equal(10_000_000ul, ArsenalConfig.UnlockCost(5));
            Assert.Equal(15_000_000ul, ArsenalConfig.UnlockCost(6));
            Assert.Equal(15_000_000ul, ArsenalConfig.UnlockCost(7));
            Assert.Equal(20_000_000ul, ArsenalConfig.UnlockCost(8));
            Assert.Equal(60_000_000ul, ArsenalConfig.EnhanceCost(1));
            Assert.Equal(100_000_000ul, ArsenalConfig.EnhanceCost(2));
        }

        [Fact]
        public void BattlePowerTopFiveOverlordAndSharedByRank()
        {
            var arsenal = new GuildArsenal();
            var members = new Dictionary<uint, GuildMember>
            {
                [1] = new GuildMember(1, "Leader", 130, GuildRank.Leader, 20261124),
                [2] = new GuildMember(2, "Deputy", 130, GuildRank.DeputyLeader, 20261124),
            };

            // Unlock 6 poles: 5 with 2M+ (1 BP each, one with 5M+ Overlord = 2 BP), 6th with 2M (ignored by top 5)
            uint nextId = 1;
            void AddSuperPlus12TwoSock(TotemPole pole, uint owner, int count)
            {
                for (int i = 0; i < count; i++)
                    pole.Items[nextId] = new TotemEntry(nextId++, owner, "Owner", pole.Type, 410009, 12, 255, 255);
            }

            // 4 items * 1,362,330 = 5,449,320 -> 2 BP + Overlord
            var p0 = arsenal.Unlock(TotemPoleType.Headgear);
            AddSuperPlus12TwoSock(p0, 1, 4);
            // 8 items * 1,362,330 = 10,898,640 -> 3 BP + Overlord
            var p1 = arsenal.Unlock(TotemPoleType.Armor);
            AddSuperPlus12TwoSock(p1, 1, 8);
            // 2 items * 1,362,330 = 2,724,660 -> 1 BP, plus +2 enhancement (active) -> 3 BP
            var p2 = arsenal.Unlock(TotemPoleType.Weapon, enhancement: 2, expiration: 20261224);
            AddSuperPlus12TwoSock(p2, 2, 2);
            // 2 items -> 1 BP, expired enhancement ignored
            var p3 = arsenal.Unlock(TotemPoleType.Ring, enhancement: 2, expiration: 20261101);
            AddSuperPlus12TwoSock(p3, 2, 2);
            // 2 items -> 1 BP
            var p4 = arsenal.Unlock(TotemPoleType.Boots);
            AddSuperPlus12TwoSock(p4, 2, 2);
            // 2 items -> 1 BP (6th pole, excluded from top 5)
            var p5 = arsenal.Unlock(TotemPoleType.Necklace);
            AddSuperPlus12TwoSock(p5, 2, 2);

            arsenal.Recalculate(ArsenalConfig.Default, today: 20261124, members);

            // Top 5 BP: 3 (Armor) + 3 (Weapon enhanced) + 2 (Headgear) + 1 + 1 = 10
            Assert.Equal(10u, arsenal.TotalBattlePower);
            // 2 Overlord poles (Headgear, Armor) => guild level 3
            Assert.Equal((byte)3, arsenal.GuildLevel);
            // Member donations
            Assert.Equal(12u * 1_362_330u, members[1].ArsenalDonation);
            Assert.Equal(8u * 1_362_330u, members[2].ArsenalDonation);
            // Shared BP by rank
            Assert.Equal(10u, ArsenalConfig.SharedBattlePower(10, GuildRank.Leader));
            Assert.Equal(9u, ArsenalConfig.SharedBattlePower(10, GuildRank.DeputyLeader));
            Assert.Equal(2u, ArsenalConfig.SharedBattlePower(10, GuildRank.Member));
        }

        [Fact]
        public void MsgTotemPoleRoundTripsExactLayout()
        {
            var msg = new MsgTotemPole
            {
                Action = MsgTotemPole.TotemPoleAction.Enhance,
                TotemType = 2,
                ItemID = 12345,
                Enhancement = 2,
                Reserved = 0,
                EnhancementExpiration = 20261224,
            };
            var raw = msg.Encode();
            Assert.Equal(MsgTotemPole.Size, raw.Length);
            Assert.Equal((ushort)PacketType.MsgTotemPole, BitConverter.ToUInt16(raw, 2));
            Assert.Equal((ushort)3, BitConverter.ToUInt16(raw, 4));
            Assert.Equal(2u, BitConverter.ToUInt32(raw, 8));
            Assert.Equal(12345u, BitConverter.ToUInt32(raw, 12));
            Assert.Equal(2u, BitConverter.ToUInt32(raw, 16));
            Assert.Equal(20261224u, BitConverter.ToUInt32(raw, 24));

            var decoded = new MsgTotemPole();
            decoded.Decode(raw);
            Assert.Equal(msg.Action, decoded.Action);
            Assert.Equal(msg.TotemType, decoded.TotemType);
            Assert.Equal(msg.ItemID, decoded.ItemID);
            Assert.Equal(msg.Enhancement, decoded.Enhancement);
            Assert.Equal(msg.EnhancementExpiration, decoded.EnhancementExpiration);
        }

        [Fact]
        public void MsgTotemPoleInfoEncodesExactLayout()
        {
            var msg = new MsgTotemPoleInfo
            {
                TotalBattlePower = 11,
                PlayerDonation = 2_724_660,
                SharedBattlePower = 9,
            };
            msg.Poles.Add(new MsgTotemPoleInfo.PoleEntry(TotemPoleType.Weapon, 2, 1, 4_500_000, 20261224, true));
            msg.Poles.Add(new MsgTotemPoleInfo.PoleEntry(TotemPoleType.Headgear, 0, 0, 0, 0, false));

            var raw = msg.Encode();
            Assert.Equal(MsgTotemPoleInfo.HeaderSize + 2 * MsgTotemPoleInfo.EntrySize, raw.Length);
            Assert.Equal((ushort)PacketType.MsgTotemPoleInfo, BitConverter.ToUInt16(raw, 2));
            Assert.Equal((ushort)0, BitConverter.ToUInt16(raw, 4));
            Assert.Equal(11u, BitConverter.ToUInt32(raw, 8));
            Assert.Equal(2_724_660u, BitConverter.ToUInt32(raw, 12));
            Assert.Equal(9u, BitConverter.ToUInt32(raw, 16));
            Assert.Equal(2u, BitConverter.ToUInt32(raw, 20));
            // Entry 0 at 24
            Assert.Equal(2u, BitConverter.ToUInt32(raw, 24));
            Assert.Equal(2u, BitConverter.ToUInt32(raw, 28));
            Assert.Equal(1u, BitConverter.ToUInt32(raw, 32));
            Assert.Equal(4_500_000u, BitConverter.ToUInt32(raw, 36));
            Assert.Equal(20261224u, BitConverter.ToUInt32(raw, 40));
            Assert.Equal((byte)1, raw[44]);
            // Entry 1 at 48
            Assert.Equal(0u, BitConverter.ToUInt32(raw, 48));
            Assert.Equal((byte)0, raw[68]);
        }

        [Fact]
        public void MsgWeaponsInfoEncodesExactLayout()
        {
            var msg = new MsgWeaponsInfo
            {
                Action = 0,
                BeginAt = 1,
                EndAt = 8,
                TotemType = 2,
                TotalCount = 1,
                BattlePower = 1,
                Enhancement = 0,
                EnhancementExpiration = 0,
                Donation = 1_362_330,
            };
            msg.Items.Add(new MsgWeaponsInfo.WeaponEntry(
                ItemID: 99, Position: 1, OwnerName: "Hero", TypeID: 410009,
                Quality: 9, Plus: 12, Gem1: 13, Gem2: 255, BattlePower: 19, Donation: 1_362_330));

            var raw = msg.Encode();
            Assert.Equal(MsgWeaponsInfo.HeaderSize + MsgWeaponsInfo.EntrySize, raw.Length);
            Assert.Equal((ushort)PacketType.MsgWeaponsInfo, BitConverter.ToUInt16(raw, 2));
            Assert.Equal(1u, BitConverter.ToUInt32(raw, 8));
            Assert.Equal(8u, BitConverter.ToUInt32(raw, 12));
            Assert.Equal(2u, BitConverter.ToUInt32(raw, 16));
            Assert.Equal(1u, BitConverter.ToUInt32(raw, 20));
            Assert.Equal(1u, BitConverter.ToUInt32(raw, 24));
            Assert.Equal(1_362_330u, BitConverter.ToUInt32(raw, 36));
            Assert.Equal(1u, BitConverter.ToUInt32(raw, 40));
            // Entry 0 at 44
            Assert.Equal(99u, BitConverter.ToUInt32(raw, 44));
            Assert.Equal(1u, BitConverter.ToUInt32(raw, 48));
            Assert.Equal(410009u, BitConverter.ToUInt32(raw, 68));
            Assert.Equal((byte)9, raw[72]);
            Assert.Equal((byte)12, raw[73]);
            Assert.Equal((byte)13, raw[74]);
            Assert.Equal((byte)255, raw[75]);
            Assert.Equal(19u, BitConverter.ToUInt32(raw, 76));
            Assert.Equal(1_362_330u, BitConverter.ToUInt32(raw, 80));
        }

        [Fact]
        public void MsgSynpOfferAndFactionRankIncludeArsenalDonation()
        {
            var member = new GuildMember(1, "Hero", 120, GuildRank.Leader, 20261124)
            {
                SilverDonation = 500,
                ConquerPointsDonation = 100,
                ArsenalDonation = 1_362_330,
            };
            Assert.Equal(1_362_930u, member.TotalDonation);

            var offer = new MsgSynpOffer(member).Encode();
            Assert.Equal(500u, BitConverter.ToUInt32(offer, 8));
            Assert.Equal(100u, BitConverter.ToUInt32(offer, 12));
            Assert.Equal(1_362_330u, BitConverter.ToUInt32(offer, 24));

            var rank = new MsgFactionRankInfo { RankType = MsgFactionRankInfo.FactionRankType.Arsenal, MaxRankSize = 1 };
            rank.Entries.Add((member, 0u));
            var rawRank = rank.Encode();
            Assert.Equal(1_362_330u, BitConverter.ToUInt32(rawRank, 12 + 28));
            Assert.Equal(1_362_930u, BitConverter.ToUInt32(rawRank, 12 + 48));
        }
    }
}

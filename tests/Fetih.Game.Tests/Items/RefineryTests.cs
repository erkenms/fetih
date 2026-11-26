namespace Fetih.Game.Tests.Items
{
    using System;
    using System.Linq;
    using Fetih.Game.Combat;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
    using Xunit;
    using static Fetih.Game.Packets.MsgItemStatus;

    public class RefineryTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        // Excerpts of the 5619 client's files
        private const string Quench = "724440\r\n724400\r\n\r\n724515\r\n";
        private const string Tips =
            "[301]\r\nName=Intensification\r\nNum=Lvl~%d~Intensification:~+%d\r\nLevel=1\r\nPower=800\r\n\r\n" +
            "[307]\r\nName=Critical~Strike\r\nLevel=1\r\nPower=5\r\n\r\n" +
            "[317]\r\nName=Firer~Resist\r\nLevel=1\r\nPower=5\r\n\r\n" +
            "[724440]\r\nName=Intensification\r\nLevel=1\r\nPower=200\r\n\r\n" +
            "[724400]\r\nName=Critical~Strike\r\nLevel=1\r\nPower=4\r\n\r\n" +
            "[724515]\r\nName=Firer~Resist\r\nLevel=1\r\nPower=3\r\n";
        private const string Solidify = "723694 10\r\n723695 100\r\n";
        private const string Info = "[Other]\r\nlev1=99\r\n[Solidify]\r\nEquipmentOffsetX=-10   // tip\r\n" +
            "lev1=10    // level 1 needs 10\r\nlev2=30\r\nlev3=70\r\nlev4=150\r\nlev5=270\r\n\r\n[DlgItemTip]\r\namount=77\r\n";

        private static RefineryTable Table() => RefineryTable.Parse(Quench, Tips, Solidify, Info);

        [Fact]
        public void RefineryItemsTakeTheirKindByName()
        {
            var table = Table();
            Assert.Equal(3, table.Count);
            Assert.True(table.TryGet(724440, out var hp));
            Assert.Equal(new Refinement(RefineryKind.Intensification, 1, 200), hp);
            Assert.True(table.TryGet(724400, out var crit));
            Assert.Equal(new Refinement(RefineryKind.CriticalStrike, 1, 4), crit);
            Assert.True(table.TryGet(724515, out var fire));
            Assert.Equal(RefineryKind.FireResist, fire.Kind);
            Assert.False(table.TryGet(301, out _));
        }

        [Fact]
        public void StonesAndLevelsComeFromTheirFiles()
        {
            var table = Table();
            Assert.Equal(10u, table.StonePoints(723694));
            Assert.Equal(100u, table.StonePoints(723695));
            Assert.Equal(0u, table.StonePoints(724440));
            Assert.Equal(new uint[] { 10, 30, 70, 150, 270 }, Enumerable.Range(1, 5).Select(table.PointsFor));
        }

        [Theory]
        [InlineData("Headgear", "", 111003, true)]
        [InlineData("Headgear", "", 120023, false)]
        [InlineData("Necklace", "", 120023, true)]
        [InlineData("Bag", "", 121023, true)]
        [InlineData("[Necklace&Bag][FireResistance]Item", "", 121023, true)]
        [InlineData("Ring", "Right click to refine a Ring.", 151003, false)]
        [InlineData("Ring", "Right click to refine a Ring or Heavy Ring.", 151003, true)]
        [InlineData("(Heavy)Ring", "", 151003, true)]
        [InlineData("Bracelet", "", 152003, true)]
        [InlineData("Boots", "", 160003, true)]
        [InlineData("Armor", "", 130003, true)]
        [InlineData("Shield", "", 900301, true)]
        [InlineData("Bow", "", 500301, true)]
        [InlineData("2-Handed", "", 500301, false)]
        [InlineData("2-Handed", "", 511301, true)]
        [InlineData("1-Handed", "", 410301, true)]
        [InlineData("1-Handed", "", 510301, false)]
        [InlineData("Backsword", "", 421301, true)]
        public void RefineryItemsFitTheGearTheirKindNames(string kind, string description, uint gear, bool fits) =>
            Assert.Equal(fits, Refinery.Fits(new ItemType { Kind = kind, Description = description }, gear));

        [Theory]
        [InlineData(0u, 7)]
        [InlineData(1u, 8)]
        [InlineData(2u, 9)]
        [InlineData(3u, 11)]
        [InlineData(4u, 14)]
        [InlineData(6u, 14)]
        public void TheTermIsSevenDaysAndVipAddsDays(uint vip, int days)
        {
            var gear = new DbItem { ItemID = 7 };
            Refinery.Apply(gear, new Refinement(RefineryKind.CriticalStrike, 2, 6), Now, vip);
            Assert.Equal(Now.AddDays(days), gear.RefineryExpiresAt);
            Assert.Equal((uint)RefineryKind.CriticalStrike, gear.RefineryKind);
            Assert.Equal(2, gear.RefineryLevel);
            Assert.Equal(6u, gear.RefineryPower);
        }

        [Fact]
        public void TheTemporaryStateHidesThePermanentOneUntilItEnds()
        {
            var gear = new DbItem
            {
                StableKind = (uint)RefineryKind.Intensification, StableLevel = 1, StablePower = 200,
                RefineryKind = (uint)RefineryKind.Block, RefineryLevel = 2, RefineryPower = 8,
                RefineryExpiresAt = Now.AddHours(1),
            };
            Assert.Equal((RefineryKind.Block, 8u), Refinery.Active(gear, Now));
            Assert.Equal((RefineryKind.Intensification, 200u), Refinery.Active(gear, Now.AddHours(1)));
            Assert.Equal(8, Refinery.Bonus(new[] { gear, gear }.Take(1), RefineryKind.Block, Now));
            Assert.Equal(0, Refinery.Bonus(new[] { gear }, RefineryKind.Block, Now.AddHours(2)));
        }

        [Fact]
        public void StabilizingNeedsTheLevelsPointsLessAPermanentLowerLevels()
        {
            var table = Table();
            var gear = new DbItem { RefineryKind = 307, RefineryLevel = 3 };
            Assert.Equal((70u, 0u), Refinery.Points(gear, table));
            gear.StableKind = 313;
            gear.StableLevel = 2;
            Assert.Equal((70u, 30u), Refinery.Points(gear, table));
            gear.StableLevel = 4;
            Assert.Equal((10u, 0u), Refinery.Points(gear, table));
        }

        [Fact]
        public void StabilizingMovesTheTemporaryStateToThePermanentOne()
        {
            var gear = new DbItem { RefineryKind = 307, RefineryLevel = 3, RefineryPower = 8, RefineryExpiresAt = Now };
            Refinery.Stabilize(gear);
            Assert.Equal((307u, (byte)3, 8u), (gear.StableKind, gear.StableLevel, gear.StablePower));
            Assert.Equal(0u, gear.RefineryKind);
            Assert.Null(gear.RefineryExpiresAt);
        }

        [Fact]
        public void ThePermanentStateGoesBeforeTheTemporaryOne()
        {
            var gear = new DbItem
            {
                ItemID = 9, StableKind = 301, StableLevel = 1, StablePower = 200,
                RefineryKind = 307, RefineryLevel = 2, RefineryPower = 6, RefineryExpiresAt = Now.AddSeconds(90.5),
            };
            Assert.Equal(new[]
            {
                new Entry(9, StatusMode.Stable, 301, 1, 200, 0),
                new Entry(9, StatusMode.Refine, 307, 2, 6, 91),
            }, Refinery.Entries(gear, Now));
            Assert.Single(Refinery.Entries(gear, Now.AddDays(1)));
            Assert.False(Refinery.HasState(new DbItem { RefineryKind = 307, RefineryExpiresAt = Now }, Now));
        }

        [Fact]
        public void ItemStatusEntriesAre28BytesFrom8()
        {
            var bytes = new MsgItemStatus(new Entry(1000, StatusMode.Stabilized, 301, 2, 500, 0),
                new Entry(1001, StatusMode.Refine, 307, 1, 4, 604800)).Encode();
            Assert.Equal(8 + 2 * MsgItemStatus.EntrySize, bytes.Length);
            Assert.Equal(2077, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            var second = Enumerable.Range(0, 7).Select(i => BitConverter.ToUInt32(bytes, 36 + 4 * i));
            Assert.Equal(new uint[] { 1001, 0, 307, 1, 4, 604800, 0 }, second);
            Assert.Equal(4u, BitConverter.ToUInt32(bytes, 12));
        }

        [Fact]
        public void QuenchHasTheGearAt8AndTheRefineryItemAt12()
        {
            var msg = new MsgQuench();
            msg.Decode(Convert.FromHexString("10001C080000000040420F0041420F00" + "5451436C69656E74"));
            Assert.Equal(MsgQuench.RefineMode, msg.Mode);
            Assert.Equal(1000000u, msg.TargetID);
            Assert.Equal(1000001u, msg.RefineryItemID);
        }

        [Fact]
        public void SolidifyListsTheStonesFrom16()
        {
            var msg = new MsgSolidify();
            msg.Decode(Convert.FromHexString("1C000E040000000040420F000200000041420F0042420F00" + "5451436C69656E74"));
            Assert.Equal(1000000u, msg.EquipmentID);
            Assert.Equal(new uint[] { 1000001, 1000002 }, msg.StoneIDs);

            // A count past the packet's end only reads what's there
            msg.Decode(Convert.FromHexString("14000E040000000040420F00FF00000041420F00"));
            Assert.Equal(new uint[] { 1000001 }, msg.StoneIDs);
        }

        [Fact]
        public void BlockHalvesAHitAtItsChance()
        {
            Assert.Equal(50u, Damage.ApplyBlock(100, 100, new Random(1)));
            Assert.Equal(100u, Damage.ApplyBlock(100, 0, new Random(1)));
            Assert.Equal(1u, Damage.ApplyBlock(1, 100, new Random(1)));
        }

        [Fact]
        public void TheStatWindowShowsBlockInHundredthsAndBreakthroughInTenths()
        {
            var stats = new Stats(1, 1, 0, 0, 1, 1000, false) { Block = 8, Breakthrough = 12, Counteraction = 10 };
            var bytes = new MsgPlayerAttribInfo(1, 1, 1, 1, stats).Encode();
            Assert.Equal(new uint[] { 800, 120, 100 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt32(bytes, 84 + 4 * i)));
        }
    }
}

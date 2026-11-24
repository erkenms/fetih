namespace Comet.Game.Tests.Items
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Magic;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class UsingTests
    {
        [Fact]
        public void ExpBallsUsedCountOnlyOnTheirDay()
        {
            var today = new DateTime(2026, 11, 21);
            var character = new DbCharacter(1, "Hero") { ExpBallDay = today, ExpBallsUsed = 7 };
            Assert.Equal(7, Using.ExpBallsUsedOn(character, today.AddHours(23)));
            Assert.Equal(0, Using.ExpBallsUsedOn(character, today.AddDays(1)));
            Assert.Equal(0, Using.ExpBallsUsedOn(new DbCharacter(1, "Hero"), today));
        }

        [Fact]
        public void DoubleExpStacksSameRateUpgradesHigherAndRejectsLower()
        {
            uint id = 990001;
            var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            DoubleExp.Clear(id);
            try
            {
                Assert.True(DoubleExp.Apply(id, 200, TimeSpan.FromMinutes(60), now));
                Assert.Equal(200UL, DoubleExp.Scale(id, 100, now));
                Assert.Equal(3600u, DoubleExp.SecondsLeft(id, now));

                // Same rate extends remaining time
                Assert.True(DoubleExp.Apply(id, 200, TimeSpan.FromMinutes(10), now.AddMinutes(5)));
                Assert.Equal(3900u, DoubleExp.SecondsLeft(id, now.AddMinutes(5)));

                // Higher rate replaces
                Assert.True(DoubleExp.Apply(id, 500, TimeSpan.FromMinutes(30), now.AddMinutes(5)));
                Assert.Equal(500UL, DoubleExp.Scale(id, 100, now.AddMinutes(5)));
                Assert.Equal(1800u, DoubleExp.SecondsLeft(id, now.AddMinutes(5)));

                // Lower rate is rejected while higher rate is active
                Assert.False(DoubleExp.Apply(id, 200, TimeSpan.FromMinutes(60), now.AddMinutes(10)));
                Assert.Equal(500UL, DoubleExp.Scale(id, 100, now.AddMinutes(10)));

                // Once expired, normal rate applies and a lower rate can start again
                var later = now.AddMinutes(40);
                Assert.Equal(100UL, DoubleExp.Scale(id, 100, later));
                Assert.True(DoubleExp.Apply(id, 200, TimeSpan.FromMinutes(10), later));
                Assert.Equal(200UL, DoubleExp.Scale(id, 100, later));
            }
            finally
            {
                DoubleExp.Clear(id);
            }
        }

        [Fact]
        public void DoubleExpAttributePacksSecondsAndPercentInClientSlots()
        {
            var bytes = MsgUserAttrib.DoubleExpOf(1000001, 3600, 200).Encode();
            Assert.Equal(18u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(3600u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 24));
            Assert.Equal(200u, BitConverter.ToUInt32(bytes, 28));
        }

        [Fact]
        public void ItemUseTablesCoverEveryDocumentedGroup()
        {
            Assert.True(DoubleExp.TryGetPotion(723017, out var exp));
            Assert.Equal((200u, TimeSpan.FromMinutes(60)), (exp.Percent, exp.Duration));

            Assert.True(Using.TryGetPack(720010, out var amritaBundle));
            Assert.Equal((1000030u, 3), (amritaBundle[0].TypeID, amritaBundle[0].Count));

            Assert.True(Using.TryGetSilverBag(723713, out var silver));
            Assert.Equal(300_000u, silver);

            Assert.True(Using.TryGetConquerPointPack(729911, out var cps));
            Assert.Equal(3u, cps);

            Assert.True(Using.TryGetHairDye(1060030, out var black));
            Assert.Equal((byte)3, black);
            Assert.True(Using.TryGetHairDye(1060090, out var white));
            Assert.Equal((byte)9, white);

            Assert.True(Using.TryGetFirework(720030, out var plain));
            Assert.Null(plain);
            Assert.True(Using.TryGetFirework(720031, out var love));
            Assert.Equal("Endless Love", love);

            Assert.True(Using.TryGetPkReducer(720128, out var amulet));
            Assert.Equal((30, 30, true, TimeSpan.FromHours(1)),
                ((int)amulet.Amount, (int)amulet.MinPoints, amulet.ForbidJail, amulet.Cooldown));

            Assert.True(Using.TryGetTransform(722498, out var dove));
            Assert.Equal(new[] { 2u }, dove.MonsterTypeIDs);
            Assert.Equal(300u, dove.Seconds);

            // Alternate weapon skill books map to their spells
            Assert.Equal(Learning.WideStrike, Learning.BookOf(721278));
            Assert.Equal(Learning.Halt, Learning.BookOf(721273));
            Assert.Equal(Learning.Phoenix, Learning.BookOf(721287));
        }
    }
}

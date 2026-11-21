namespace Comet.Game.Tests.Items
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
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
    }
}

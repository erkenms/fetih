namespace Fetih.Game.Tests.Training
{
    using System;
    using Fetih.Game.Database.Models;
    using Fetih.Game.States;
    using Fetih.Game.Training;
    using Xunit;

    public class TrainingTests
    {
        // The cap is level 3
        private static readonly ExperienceTable Table = new ExperienceTable(new[]
        {
            new LevelExp { Level = 1, Experience = 100, UpLevTime = 10 },
            new LevelExp { Level = 2, Experience = 1000, UpLevTime = 100 },
        });

        private static readonly DateTime Now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(1, 0, 5, 50)]       // half of level 1's time
        [InlineData(1, 50, 5, 50)]      // exactly the rest of level 1
        [InlineData(1, 0, 60, 600)]     // level 1, then half of level 2
        [InlineData(2, 0, 1000, 1000)]  // stops at the cap
        public void TimeTurnsIntoExperienceAcrossLevels(byte level, ulong experience, double time, ulong expected) =>
            Assert.Equal(expected, Table.ExperienceOfTime(level, experience, time));

        [Fact]
        public void ExperienceTurnsIntoTimeAtTheLevel()
        {
            Assert.Equal(50, Table.TimeOfExperience(2, 500));
            Assert.Equal(0, Table.TimeOfExperience(3, 500));
        }

        [Theory]
        [InlineData(0, 900)]
        [InlineData(1, 960)]
        [InlineData(6, 1440)]
        [InlineData(9, 1440)]
        public void OfflineTrainingLastsLongerForVips(uint vip, int minutes) =>
            Assert.Equal(minutes, OfflineTraining.MaxMinutes(new DbCharacter(1, "Vip") { VIPLevel = vip }));

        [Fact]
        public void OfflineTrainingCountsMinutesUpToTheMaximum()
        {
            var character = new DbCharacter(1, "Trainee");
            Assert.False(OfflineTraining.IsTraining(character));
            Assert.Equal(0, OfflineTraining.TrainedMinutes(character, Now));
            character.OfflineTrainingSince = Now.AddMinutes(-90.5);
            Assert.Equal(90, OfflineTraining.TrainedMinutes(character, Now));
            character.OfflineTrainingSince = Now.AddDays(-2);
            Assert.Equal(900, OfflineTraining.TrainedMinutes(character, Now));
        }
    }
}

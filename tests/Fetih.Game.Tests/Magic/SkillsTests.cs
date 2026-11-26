namespace Fetih.Game.Tests.Magic
{
    using Fetih.Game.Magic;
    using Xunit;

    public class SkillsTests
    {
        [Theory]
        [InlineData(0, 0, 10, false)]
        [InlineData(0, 0, 100, true)]
        [InlineData(0, 4, 10, false)]   // below half the old level: by experience as usual
        [InlineData(2, 4, 10, true)]    // from half of it, any experience
        [InlineData(3, 4, 1, true)]
        [InlineData(4, 4, 10, false)]   // back at it
        [InlineData(0, 1, 1, true)]
        public void RestartedSpellsRegainTheirOldLevelFastFromHalfOfIt(ushort level, ushort oldLevel, ulong experience, bool up) =>
            Assert.Equal(up, Casting.LevelsUp(new SkillLevel(level, 0, oldLevel), experience, 100));

        [Fact]
        public void ForgottenSpellsAreSavedForDeletionUntilLearnedAgain()
        {
            var skills = new Skills();
            skills.SetSpell(1115, new SkillLevel(3, 0));
            Assert.True(skills.ForgetSpell(1115));
            Assert.False(skills.ForgetSpell(1115));
            Assert.Equal(new ushort[] { 1115 }, skills.Snapshot().Forgotten);
            Assert.Empty(skills.Snapshot().Spells);

            skills.SetSpell(1115, new SkillLevel(0, 0));
            Assert.Empty(skills.Snapshot().Forgotten);
        }
    }
}

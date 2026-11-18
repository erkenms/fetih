namespace Comet.Game.Tests.States
{
    using Comet.Game.States;
    using Xunit;

    /// <summary>
    /// Character names are 4 to 15 printable ASCII characters without spaces, and can't
    /// carry the staff tags.
    /// </summary>
    public class CharacterRulesTests
    {
        [Theory]
        [InlineData("Hero")]
        [InlineData("Abcd")]
        [InlineData("FifteenCharsMax")]
        [InlineData("~*Dark_Lord*~")]
        [InlineData("#01Smile#02")]
        [InlineData("x.X-[Pro]")]
        public void AcceptsNames(string name)
        {
            Assert.Null(CharacterRules.CheckName(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Abc")]
        [InlineData("SixteenCharsLong")]
        [InlineData("Two Words")]
        [InlineData("Çağrı")]
        [InlineData("Tab\tName")]
        [InlineData("Admin[GM]")]
        [InlineData("[pm]Boss")]
        [InlineData("x[Gm]x")]
        public void RejectsNames(string name)
        {
            Assert.Equal("invalid_name", CharacterRules.CheckName(name));
        }
    }
}

namespace Fetih.Game.Tests.States
{
    using Fetih.Game.States;
    using Xunit;

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

namespace Fetih.Game.Tests.Social
{
    using Fetih.Game.Social;
    using Xunit;

    public class GuildTests
    {
        [Theory]
        [InlineData(10, 0x01u)]
        [InlineData(25, 0x02u)]
        [InlineData(100, 0x04u)]
        [InlineData(145, 0x04u)]
        [InlineData(41, 0x08u)]
        [InlineData(55, 0x10u)]
        [InlineData(60, 0x20u)]
        [InlineData(70, 0x40u)]
        [InlineData(75, 0x40u)]
        public void EachLineHasTheBitOfItsCheckbox(ushort currentClass, uint bit)
        {
            Assert.Equal(bit, Guild.ProfessionBit(currentClass));
        }
    }
}

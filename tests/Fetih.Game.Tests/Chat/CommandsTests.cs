namespace Fetih.Game.Tests.Chat
{
    using System.Linq;
    using Fetih.Game.Chat;
    using Xunit;

    public class CommandsTests
    {
        [Fact]
        public void ParseSplitsNameArgumentsAndText()
        {
            var (name, args, text) = Commands.Parse("/TP  1002   400 300 ");
            Assert.Equal("tp", name);
            Assert.Equal(new[] { "1002", "400", "300" }, args);
            Assert.Equal("1002   400 300", text);
        }

        [Fact]
        public void ParseWithoutArguments()
        {
            var (name, args, text) = Commands.Parse("/online");
            Assert.Equal("online", name);
            Assert.Empty(args);
            Assert.Equal("", text);
        }

        [Fact]
        public void LinesStayShortEnoughForOneChatLine()
        {
            var names = Enumerable.Range(0, 100).Select(i => "Player" + i).ToList();
            var lines = Commands.Lines("100 online:", names).ToList();
            Assert.True(lines.Count > 1);
            Assert.All(lines, l => Assert.True(l.Length <= 200));
            Assert.StartsWith("100 online: Player0, Player1", lines[0]);
            Assert.Equal(names, string.Join(", ", lines).Substring("100 online: ".Length).Split(", "));
        }
    }
}

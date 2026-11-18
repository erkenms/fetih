namespace Comet.Integration.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    /// <summary>
    /// Creating a character on the game server: every class and body, the name rules,
    /// and two players wanting the same name.
    /// </summary>
    public class CharacterCreationTests : IntegrationTest
    {
        public CharacterCreationTests(ServerFixture servers) : base(servers) { }

        private Task CreateFailedAsync(string reason) =>
            this.Servers.WaitForEventAsync("game", "character.create_failed", "reason", reason);

        public static IEnumerable<object[]> ClassesAndBodies() =>
            from profession in new ushort[] { 10, 20, 40, 50, 60, 70, 100 }
            from mesh in new ushort[] { 1003, 1004, 2001, 2002 }
            select new object[] { profession, mesh };

        [Theory]
        [MemberData(nameof(ClassesAndBodies))]
        public async Task EveryClassAndBodyCanBeCreated(ushort profession, ushort mesh)
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
            {
                var name = this.Servers.NewCharacterName();
                Assert.Equal(AnswerOk, await game.CreateCharacterAsync(name, mesh, profession, (uint)token));

                // The client sends MsgConnect again and enters the game
                var result = await game.LoginAsync(token);
                Assert.True(result.InGame);
                Assert.Equal(name, result.User.Name);
                Assert.Equal(profession, result.User.Class);
                Assert.Equal(mesh, result.User.Mesh % 10000);
                Assert.Equal(1, result.User.Level);
                Assert.Equal(318, result.User.HealthPoints);
                Assert.Equal(1000u, result.User.Silver);

                var spawn = await game.EnterMapAsync(result.User.Id);
                Assert.Equal(1010u, spawn.Command);
                Assert.Equal((61, 109), (spawn.X, spawn.Y));

                var created = await this.Servers.WaitForEventAsync("game", "character.created", "name", name);
                Assert.Equal(profession, created.GetProperty("class").GetUInt16());
                await this.Servers.WaitForEventAsync("game", "session.login", "name", name);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(11)]
        [InlineData(30)]
        [InlineData(80)]
        [InlineData(132)]
        public async Task UnknownClassIsRefused(ushort profession)
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
            {
                Assert.Equal(InvalidCharacter, await game.CreateCharacterAsync(
                    this.Servers.NewCharacterName(), 1003, profession, (uint)token));
                await CreateFailedAsync("invalid_input");

                // The player can try again on the same connection
                Assert.Equal(AnswerOk, await game.CreateCharacterAsync(
                    this.Servers.NewCharacterName(), 1003, 10, (uint)token));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1005)]
        [InlineData(2003)]
        public async Task UnknownBodyIsRefused(ushort mesh)
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
                Assert.Equal(InvalidCharacter, await game.CreateCharacterAsync(
                    this.Servers.NewCharacterName(), mesh, 10, (uint)token));
        }

        public static IEnumerable<object[]> BadNames() => new[]
        {
            new object[] { new byte[0] },
            new object[] { Encoding.ASCII.GetBytes("Ab") },
            new object[] { Encoding.ASCII.GetBytes("SixteenCharsLong") },
            new object[] { Encoding.ASCII.GetBytes("Two Words") },
            new object[] { new byte[] { 0xC7, 0x61, 0xF0, 0x72, 0xFD } },       // Çağrı in Windows-1254
            new object[] { Encoding.ASCII.GetBytes("Tab\tName") },
            new object[] { Encoding.ASCII.GetBytes("Boss[GM]") },
            new object[] { Encoding.ASCII.GetBytes("[pm]Boss") },
        };

        [Theory]
        [MemberData(nameof(BadNames))]
        public async Task NamesBreakingTheRulesAreRefused(byte[] name)
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
            {
                Assert.Equal(InvalidName, await game.CreateCharacterAsync(name, 1003, 10, (uint)token));
                await CreateFailedAsync("invalid_name");
                Assert.Equal(AnswerOk, await game.CreateCharacterAsync(
                    this.Servers.NewCharacterName(), 1003, 10, (uint)token));
            }
        }

        [Theory]
        [InlineData("~*Star*~")]
        [InlineData("#01Smile#02")]
        [InlineData("x.X-[Pro]_9")]
        public async Task NamesWithSymbolsAreAllowed(string prefix)
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
            {
                var name = this.Servers.NewCharacterName(prefix);
                Assert.Equal(AnswerOk, await game.CreateCharacterAsync(name, 1003, 10, (uint)token));
                Assert.Equal(name, (await game.LoginAsync(token)).User.Name);
            }
        }

        [Fact]
        public async Task TakenNameIsRefusedInAnyCase()
        {
            var (first, _) = await this.PlayingAsync();
            await using (first)
            {
                var (game, token, _) = await this.AtCharacterCreationAsync();
                await using (game)
                {
                    Assert.Equal(NameTaken, await game.CreateCharacterAsync(
                        first.User.Name, 1003, 10, (uint)token));
                    Assert.Equal(NameTaken, await game.CreateCharacterAsync(
                        first.User.Name.ToUpperInvariant(), 1003, 10, (uint)token));
                    await CreateFailedAsync("name_taken");
                }
            }
        }

        [Fact]
        public async Task OnlyOneOfTwoPlayersRacingForANameGetsIt()
        {
            for (int round = 0; round < 5; round++)
            {
                var a = await this.AtCharacterCreationAsync();
                var b = await this.AtCharacterCreationAsync();
                await using (a.Game)
                await using (b.Game)
                {
                    var name = this.Servers.NewCharacterName("Race");
                    var answers = await Task.WhenAll(
                        a.Game.CreateCharacterAsync(name, 1003, 10, (uint)a.Token),
                        b.Game.CreateCharacterAsync(name, 2001, 40, (uint)b.Token));
                    Assert.Single(answers, answer => answer == AnswerOk);
                    Assert.Single(answers, answer => answer == NameTaken);
                }
            }
        }

        [Fact]
        public async Task CreationWithTheWrongTokenIsRefused()
        {
            var (game, token, _) = await this.AtCharacterCreationAsync();
            await using (game)
            {
                Assert.Equal(InvalidCharacter, await game.CreateCharacterAsync(
                    this.Servers.NewCharacterName(), 1003, 10, (uint)token + 1));
                Assert.True(await game.WaitClosedAsync());
                await CreateFailedAsync("invalid_token");
            }
        }

        [Fact]
        public async Task AccountWithACharacterCantCreateAnother()
        {
            var (player, _) = await this.PlayingAsync();
            await using (player)
            {
                Assert.Equal(InvalidCharacter, await player.Game.CreateCharacterAsync(
                    this.Servers.NewCharacterName(), 1003, 10, 0));
                Assert.True(await player.Game.WaitClosedAsync());
            }
        }
    }
}

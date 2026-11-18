namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    /// <summary>
    /// Base class for integration tests: gives each test the shared servers and helpers
    /// that take a player to a given step of the login.
    /// </summary>
    [Collection(ServerCollection.Name)]
    public abstract class IntegrationTest
    {
        // Messages the game server sends in MsgTalk
        public const string AnswerOk = "ANSWER_OK";
        public const string NewRole = "NEW_ROLE";
        public const string InvalidLogin = "Invalid login";
        public const string InvalidCharacter = "Invalid character";
        public const string InvalidName = "Invalid character name";
        public const string NameTaken = "Character name taken";

        // MsgConnectEx rejection codes
        public const uint WrongPassword = 1;
        public const uint RealmOffline = 10;
        public const uint Banned = 12;
        public const uint Locked = 22;

        public const string Password = "secret1";
        protected readonly ServerFixture Servers;

        protected IntegrationTest(ServerFixture servers) => this.Servers = servers;

        /// <summary>
        /// Creates an account and logs it in up to the character creation screen. The
        /// game client is left connected, waiting for MsgRegister.
        /// </summary>
        protected async Task<(GameClient Game, ulong Token, string Email)> AtCharacterCreationAsync()
        {
            var email = await this.Servers.CreateAccountAsync(Password);
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.True(login.Accepted, $"Login refused with code {login.RejectCode}");

            var game = await this.Servers.ConnectGameAsync();
            var result = await game.LoginAsync(login.Token);
            Assert.Equal(NewRole, result.Message);
            return (game, login.Token, email);
        }

        /// <summary>Creates an account with a character standing on the map.</summary>
        protected async Task<(Player Player, string Email)> PlayingAsync(
            ushort mesh = 1003, ushort profession = 10)
        {
            var email = await this.Servers.CreateAccountAsync(Password);
            var player = await this.Servers.PlayAsync(email, Password,
                new NewCharacter(this.Servers.NewCharacterName(), mesh, profession));
            return (player, email);
        }
    }
}

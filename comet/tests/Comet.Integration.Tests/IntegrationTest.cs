namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

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

        // Authority as in the account_authority table: 1 player, 2 moderator, 3 game master
        protected async Task<(Player Player, string Email)> PlayingAtAsync(
            uint mapID, int x, int y, ushort authority = 1, byte level = 1, ushort strength = 0)
        {
            var (first, email) = await this.PlayingAsync();
            var name = first.User.Name;
            await first.DisposeAsync();

            // Wait for the logout save, or it could overwrite the position set below
            await this.Servers.WaitForLogoutSaveAsync(name);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET MapID = @map, X = @x, Y = @y, Level = @level WHERE Name = @name",
                ("@map", mapID), ("@x", x), ("@y", y), ("@level", level), ("@name", name));
            if (strength > 0)
                await this.Servers.ExecuteGameSqlAsync("UPDATE `character` SET Strength = @strength WHERE Name = @name",
                    ("@strength", strength), ("@name", name));
            await this.Servers.ExecuteAccountSqlAsync(
                "UPDATE account SET AuthorityID = @authority WHERE Username = @email",
                ("@authority", authority), ("@email", email));
            return (await this.Servers.PlayAsync(email, Password), email);
        }

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

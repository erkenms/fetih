namespace Fetih.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Xunit;

    public class AccountLoginTests : IntegrationTest
    {
        public AccountLoginTests(ServerFixture servers) : base(servers) { }

        private Task SetStatusAsync(string email, int status) =>
            this.Servers.ExecuteAccountSqlAsync(
                "UPDATE account SET StatusID = @status WHERE Username = @name",
                ("@status", status), ("@name", email));

        private Task<System.Text.Json.JsonElement> LoginFailedAsync(string email, string reason) =>
            this.Servers.WaitForEventAsync("account", "account.login_failed",
                e => e.GetProperty("username").GetString() == email
                    && e.GetProperty("reason").GetString() == reason);

        [Fact]
        public async Task RightPasswordRedirectsToTheGameServer()
        {
            var email = await this.Servers.CreateAccountAsync();
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.True(login.Accepted);
            Assert.True(login.Challenged);
            Assert.NotEqual(0u, (uint)login.Token);
            Assert.Equal(ServerFixture.Host, login.GameHost);
            Assert.Equal(this.Servers.GamePort, login.GamePort);
            var logged = await this.Servers.WaitForEventAsync("account", "account.login", "username", email);
            Assert.Equal(ServerFixture.Realm, logged.GetProperty("realm").GetString());
        }

        [Fact]
        public async Task WrongPasswordIsRefusedAndTheConnectionClosed()
        {
            var email = await this.Servers.CreateAccountAsync();
            await using var account = await this.Servers.ConnectAccountAsync();
            var login = await account.LoginAsync(email, "wrong-password", ServerFixture.Realm);
            Assert.False(login.Accepted);
            Assert.True(login.Challenged);
            Assert.Equal(WrongPassword, login.RejectCode);
            Assert.True(await account.WaitClosedAsync());
            await LoginFailedAsync(email, "bad_password");
        }

        [Fact]
        public async Task EmptyPasswordIsRefused()
        {
            var email = await this.Servers.CreateAccountAsync();
            var login = await this.Servers.LoginAsync(email, "");
            Assert.False(login.Accepted);
            Assert.Equal(WrongPassword, login.RejectCode);
            await LoginFailedAsync(email, "bad_password");
        }

        [Fact]
        public async Task PasswordIsCaseSensitive()
        {
            var email = await this.Servers.CreateAccountAsync("Secret1");
            Assert.Equal(WrongPassword, (await this.Servers.LoginAsync(email, "secret1")).RejectCode);
            Assert.True((await this.Servers.LoginAsync(email, "Secret1")).Accepted);
        }

        [Fact]
        public async Task UnknownAccountIsRefusedWithoutAChallenge()
        {
            var email = this.Servers.NewEmail("nobody");
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.False(login.Accepted);
            Assert.False(login.Challenged);
            Assert.Equal(WrongPassword, login.RejectCode);
            await LoginFailedAsync(email, "unknown_account");
        }

        [Fact]
        public async Task AccountNameMatchesRegardlessOfCase()
        {
            var email = await this.Servers.CreateAccountAsync();
            Assert.True((await this.Servers.LoginAsync(email.ToUpperInvariant(), Password)).Accepted);
        }

        [Fact]
        public async Task AccountWithoutAPasswordIsRefused()
        {
            var email = this.Servers.NewEmail("nopassword");
            await this.Servers.ExecuteAccountSqlAsync(
                "INSERT INTO account (Username) VALUES (@name)", ("@name", email));
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.False(login.Accepted);
            Assert.False(login.Challenged);
            Assert.Equal(WrongPassword, login.RejectCode);
            await LoginFailedAsync(email, "password_not_set");
        }

        [Fact]
        public async Task UnknownRealmIsRefusedAsOffline()
        {
            var email = await this.Servers.CreateAccountAsync();
            var login = await this.Servers.LoginAsync(email, Password, realm: "Nowhere");
            Assert.False(login.Accepted);
            Assert.Equal(RealmOffline, login.RejectCode);
            await LoginFailedAsync(email, "realm_offline");
        }

        [Fact]
        public async Task AccountBelowTheRealmsAuthorityIsRefused()
        {
            var email = await this.Servers.CreateAccountAsync();
            var login = await this.Servers.LoginAsync(email, Password, realm: ServerFixture.StaffRealm);
            Assert.False(login.Accepted);
            Assert.Equal(RealmLocked, login.RejectCode);
            await LoginFailedAsync(email, "realm_authority");
        }

        [Fact]
        public async Task AccountAtTheRealmsAuthorityCanLogIn()
        {
            var email = await this.Servers.CreateAccountAsync();
            await this.Servers.ExecuteAccountSqlAsync(
                "UPDATE account SET AuthorityID = 3 WHERE Username = @name", ("@name", email));
            var login = await this.Servers.LoginAsync(email, Password, realm: ServerFixture.StaffRealm);
            Assert.True(login.Accepted);
        }

        [Theory]
        [InlineData(5, Banned, "account_banned")]
        [InlineData(4, Locked, "account_locked")]
        public async Task BannedAndLockedAccountsAreRefused(int status, uint code, string reason)
        {
            var email = await this.Servers.CreateAccountAsync();
            await SetStatusAsync(email, status);
            var login = await this.Servers.LoginAsync(email, Password);
            Assert.False(login.Accepted);
            Assert.Equal(code, login.RejectCode);
            await LoginFailedAsync(email, reason);
        }

        [Fact]
        public async Task BanIsOnlyRevealedToSomeoneWithThePassword()
        {
            var email = await this.Servers.CreateAccountAsync();
            await SetStatusAsync(email, 5);
            Assert.Equal(WrongPassword, (await this.Servers.LoginAsync(email, "wrong-password")).RejectCode);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task ActivatedAndLimitedAccountsCanLogIn(int status)
        {
            var email = await this.Servers.CreateAccountAsync();
            await SetStatusAsync(email, status);
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
        }

        [Fact]
        public async Task ProofWithoutAChallengeClosesTheConnection()
        {
            int mark = this.Servers.Mark("account");
            await using var account = await this.Servers.ConnectAccountAsync();
            await account.SendAsync(AccountClient.BuildProof(new byte[32], new byte[20]));
            Assert.Null(await account.ReceiveAsync());
            await this.Servers.WaitForEventAsync("account", "net.disconnect", "reason", "protocol_error", mark);
        }

        [Fact]
        public async Task SameAccountCanLogInFromTwoConnectionsAtOnce()
        {
            var email = await this.Servers.CreateAccountAsync();
            var logins = await Task.WhenAll(
                this.Servers.LoginAsync(email, Password), this.Servers.LoginAsync(email, Password));
            Assert.All(logins, l => Assert.True(l.Accepted));
            Assert.NotEqual(logins[0].Token, logins[1].Token);
        }
    }
}

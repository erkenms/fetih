namespace Comet.Integration.Tests
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Account.Database.Repositories;
    using Xunit;

    public class AccountCreationTests : IntegrationTest
    {
        public AccountCreationTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task CreateAccountCommandMakesAnAccountThatCanLogIn()
        {
            var email = this.Servers.NewEmail();
            Assert.Equal(0, await this.Servers.RunAccountCommandAsync("create-account", email, Password));
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
            await this.Servers.WaitForEventAsync("account-cli", "account.created", "username", email);
        }

        [Fact]
        public async Task CreateAccountCommandRefusesATakenName()
        {
            var email = await this.Servers.CreateAccountAsync();
            Assert.Equal(1, await this.Servers.RunAccountCommandAsync("create-account", email, "other1"));
            await this.Servers.WaitForEventAsync("account-cli", "account.create_failed",
                e => e.GetProperty("username").GetString() == email
                    && e.GetProperty("reason").GetString() == "username_taken");

            // The first password still works
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("player@localhost")]
        [InlineData("two words@example.com")]
        [InlineData("player@@example.com")]
        public async Task RefusesNamesThatArentEmailAddresses(string username)
        {
            var (account, error) = await AccountsRepository.CreateAsync(username, Password);
            Assert.Null(account);
            Assert.Equal("invalid_username", error);
        }

        [Fact]
        public async Task RefusesNamesLongerThanTheClientSends()
        {
            var email = new string('a', 120) + "@x.example";
            var (account, error) = await AccountsRepository.CreateAsync(email, Password);
            Assert.Null(account);
            Assert.Equal("invalid_username", error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("12345")]
        [InlineData("seventeen-chars-x")]
        [InlineData("şifre12")]
        public async Task RefusesPasswordsOutsideTheRules(string password)
        {
            var (account, error) = await AccountsRepository.CreateAsync(this.Servers.NewEmail(), password);
            Assert.Null(account);
            Assert.Equal("invalid_password", error);
        }

        [Fact]
        public async Task NamesDifferingOnlyInCaseAreTheSameAccount()
        {
            var email = await this.Servers.CreateAccountAsync();
            var (account, error) = await AccountsRepository.CreateAsync(email.ToUpperInvariant(), Password);
            Assert.Null(account);
            Assert.Equal("username_taken", error);
        }

        [Fact]
        public async Task OnlyOneOfManySimultaneousCreationsOfANameWins()
        {
            var email = this.Servers.NewEmail();
            var results = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => AccountsRepository.CreateAsync(email, Password))));
            Assert.Single(results, r => r.Account != null);
            Assert.All(results.Where(r => r.Account == null),
                r => Assert.Equal("username_taken", r.Error));
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
        }

        [Fact]
        public async Task ManyDifferentAccountsCanBeCreatedAtOnce()
        {
            var emails = Enumerable.Range(0, 20).Select(_ => this.Servers.NewEmail()).ToList();
            var results = await Task.WhenAll(emails
                .Select(email => Task.Run(() => AccountsRepository.CreateAsync(email, Password))));
            Assert.All(results, r => Assert.NotNull(r.Account));
            Assert.Equal(20, results.Select(r => r.Account.AccountID).Distinct().Count());
        }

        [Fact]
        public async Task SetPasswordCommandReplacesThePassword()
        {
            var email = await this.Servers.CreateAccountAsync();
            Assert.Equal(0, await this.Servers.RunAccountCommandAsync("set-password", email, "newpass9"));
            Assert.Equal(WrongPassword, (await this.Servers.LoginAsync(email, Password)).RejectCode);
            Assert.True((await this.Servers.LoginAsync(email, "newpass9")).Accepted);
        }

        [Fact]
        public async Task SetPasswordCommandRefusesAShortPasswordAndUnknownAccounts()
        {
            var email = await this.Servers.CreateAccountAsync();
            Assert.Equal(1, await this.Servers.RunAccountCommandAsync("set-password", email, "short"));
            Assert.Equal(1, await this.Servers.RunAccountCommandAsync(
                "set-password", this.Servers.NewEmail("nobody"), "longenough"));
            Assert.True((await this.Servers.LoginAsync(email, Password)).Accepted);
        }
    }
}

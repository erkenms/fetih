namespace Comet.Account.Database.Tests
{
    using Comet.Account.Database.Repositories;
    using Xunit;

    /// <summary>
    /// Account names are email addresses (the 5619 login box takes them) and passwords
    /// are 6 to 16 printable characters (the client's password box limit).
    /// </summary>
    public class AccountRulesTests
    {
        [Theory]
        [InlineData("player@example.com")]
        [InlineData("averagelongname.test@example.com")]
        [InlineData("a.b-c_d+tag@mail.example.co.uk")]
        public void AcceptsEmailAddresses(string username)
        {
            Assert.Null(AccountRules.CheckUsername(username));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("test")]
        [InlineData("@example.com")]
        [InlineData("player@")]
        [InlineData("player@example")]
        [InlineData("player@example.")]
        [InlineData("player@.com")]
        [InlineData("player@example..com")]
        [InlineData("pla yer@example.com")]
        [InlineData("player@exa@mple.com")]
        [InlineData("çağrı@example.com")]
        public void RejectsOtherNames(string username)
        {
            Assert.Equal("invalid_username", AccountRules.CheckUsername(username));
        }

        [Fact]
        public void LimitsNameToWhatTheClientSends()
        {
            string domain = "@example.com";
            Assert.Null(AccountRules.CheckUsername(new string('a', 127 - domain.Length) + domain));
            Assert.Equal("invalid_username",
                AccountRules.CheckUsername(new string('a', 128 - domain.Length) + domain));
        }

        [Theory]
        [InlineData("secret")]
        [InlineData("sixteen-chars-ok")]
        [InlineData("with space !#$%")]
        public void AcceptsPasswords(string password)
        {
            Assert.Null(AccountRules.CheckPassword(password));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("five5")]
        [InlineData("seventeen-chars-x")]
        [InlineData("şifre123")]
        [InlineData("tab\tinside")]
        public void RejectsOtherPasswords(string password)
        {
            Assert.Equal("invalid_password", AccountRules.CheckPassword(password));
        }
    }
}

namespace Comet.Account.Database.Tests
{
    using Xunit;
    using Comet.Account.Database;

    public class ConfigurationTests
    {
        [Fact]
        public void CommandLineOverridesNetworkConfiguration()
        {
            var config = new ServerConfiguration(new[] { "--Network:Port=9961" });
            Assert.Equal(9961, config.Network.Port);
        }

        [Fact]
        public void CommandLineOverridesLoggingConfiguration()
        {
            var config = new ServerConfiguration(new[] { "--Logging:Level=warn" });
            Assert.Equal("warn", config.Logging.Level);
        }
    }
}

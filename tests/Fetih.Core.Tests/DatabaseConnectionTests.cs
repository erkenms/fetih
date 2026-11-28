namespace Fetih.Core.Tests
{
    using Fetih.Core.Database;
    using MySqlConnector;
    using Xunit;

    public class DatabaseConnectionTests
    {
        [Fact]
        public void TcpConnection_SetsServerSchemaAndPassword()
        {
            var cs = DatabaseConnection.Build("127.0.0.1", "fetih_game", "fetih", "secret");
            var parsed = new MySqlConnectionStringBuilder(cs);

            Assert.Equal("127.0.0.1", parsed.Server);
            Assert.Equal("fetih_game", parsed.Database);
            Assert.Equal("fetih", parsed.UserID);
            Assert.Equal("secret", parsed.Password);
            Assert.NotEqual(MySqlConnectionProtocol.Unix, parsed.ConnectionProtocol);
        }

        [Fact]
        public void UnixSocketConnection_SetsUnixProtocolAndOmitsEmptyPassword()
        {
            var cs = DatabaseConnection.Build("/run/mysqld/mysqld.sock", "fetih_account", "fetih-account", "");
            var parsed = new MySqlConnectionStringBuilder(cs);

            Assert.Equal("/run/mysqld/mysqld.sock", parsed.Server);
            Assert.Equal(MySqlConnectionProtocol.Unix, parsed.ConnectionProtocol);
            Assert.Equal("fetih_account", parsed.Database);
            Assert.Equal("fetih-account", parsed.UserID);
            Assert.Equal("", parsed.Password);
        }
    }
}

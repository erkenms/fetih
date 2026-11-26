namespace Fetih.Network.Sockets.Tests
{
    using Xunit;

    public class TcpServerRegistryTests
    {
        [Fact]
        public void AttemptsHaveTheirOwnLimit()
        {
            var registry = new TcpServerRegistry(maxActiveConn: 2, maxAttemptsPerMinute: 5);
            for (int i = 0; i < 5; i++)
            {
                Assert.True(registry.AddActiveClient("10.0.0.1"));
                registry.RemoveActiveClient("10.0.0.1");
            }

            Assert.False(registry.AddActiveClient("10.0.0.1"));
            Assert.True(registry.AddActiveClient("10.0.0.2"));
        }

        [Fact]
        public void TooManyOpenConnectionsBlockTheAddress()
        {
            var registry = new TcpServerRegistry(maxActiveConn: 2, maxAttemptsPerMinute: 50);
            Assert.True(registry.AddActiveClient("10.0.0.1"));
            Assert.True(registry.AddActiveClient("10.0.0.1"));
            Assert.False(registry.AddActiveClient("10.0.0.1"));

            registry.RemoveActiveClient("10.0.0.1");
            Assert.False(registry.AddActiveClient("10.0.0.1"));
        }

        [Fact]
        public void PurgeClearsAttemptsButKeepsBans()
        {
            var registry = new TcpServerRegistry(maxActiveConn: 2, maxAttemptsPerMinute: 2);
            for (int i = 0; i < 2; i++)
            {
                Assert.True(registry.AddActiveClient("10.0.0.1"));
                registry.RemoveActiveClient("10.0.0.1");
            }

            registry.TimedPurgeJob(null);
            Assert.True(registry.AddActiveClient("10.0.0.1"));
            registry.RemoveActiveClient("10.0.0.1");
            Assert.True(registry.AddActiveClient("10.0.0.1"));
            registry.RemoveActiveClient("10.0.0.1");

            Assert.False(registry.AddActiveClient("10.0.0.1"));
            registry.TimedPurgeJob(null);
            Assert.False(registry.AddActiveClient("10.0.0.1"));
        }

        [Fact]
        public void PurgeLiftsExpiredBans()
        {
            // A negative ban has already expired when the purge runs; zero could tie the clock
            var registry = new TcpServerRegistry(banMinutes: -1, maxAttemptsPerMinute: 1);
            Assert.True(registry.AddActiveClient("10.0.0.1"));
            registry.RemoveActiveClient("10.0.0.1");
            Assert.False(registry.AddActiveClient("10.0.0.1"));
            Assert.False(registry.AddActiveClient("10.0.0.1"));

            registry.TimedPurgeJob(null);
            Assert.True(registry.AddActiveClient("10.0.0.1"));
        }
    }
}

namespace Comet.Network.Sockets.Tests
{
    using Xunit;

    /// <summary>
    /// The registry limits connections per IP address: how many are open at once, and
    /// how many are opened per minute. Going over either blocks the address.
    /// </summary>
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
    }
}

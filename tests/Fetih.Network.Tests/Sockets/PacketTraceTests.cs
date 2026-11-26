namespace Fetih.Network.Sockets.Tests
{
    using System.Collections.Generic;
    using Fetih.Core.Logging;
    using Xunit;

    public class PacketTraceTests
    {
        [Fact]
        public void TracesNothingByDefault()
        {
            PacketTrace.Configure(new PacketTraceConfiguration());
            Assert.False(PacketTrace.IsTraced("10.0.0.5", 3));
            PacketTrace.Configure(null);
            Assert.False(PacketTrace.IsTraced("10.0.0.5", 3));
        }

        [Fact]
        public void TracesListedAddressesFromTheStart()
        {
            PacketTrace.Configure(new PacketTraceConfiguration { IPs = new List<string> { " 172.16.60.128 " } });
            Assert.True(PacketTrace.IsTraced("172.16.60.128", 0));
            Assert.False(PacketTrace.IsTraced("172.16.60.129", 0));
        }

        [Fact]
        public void TracesListedAccountsOnceKnown()
        {
            PacketTrace.Configure(new PacketTraceConfiguration { Accounts = new List<uint> { 3 } });
            Assert.True(PacketTrace.IsTraced("10.0.0.5", 3));
            Assert.False(PacketTrace.IsTraced("10.0.0.5", 4));
            Assert.False(PacketTrace.IsTraced("10.0.0.5", 0));
        }

        [Fact]
        public void AllTracesEveryone()
        {
            PacketTrace.Configure(new PacketTraceConfiguration { All = true });
            Assert.True(PacketTrace.IsTraced("10.0.0.5", 0));
            PacketTrace.Configure(new PacketTraceConfiguration());
        }

        [Fact]
        public void RuntimeAccountToggleAddsAndRemovesAccount()
        {
            PacketTrace.Configure(new PacketTraceConfiguration());
            Assert.False(PacketTrace.IsAccountTraced(7));

            PacketTrace.SetAccount(7, true);
            Assert.True(PacketTrace.IsAccountTraced(7));
            Assert.True(PacketTrace.IsTraced("10.0.0.5", 7));

            PacketTrace.SetAccount(7, false);
            Assert.False(PacketTrace.IsAccountTraced(7));
            Assert.False(PacketTrace.IsTraced("10.0.0.5", 7));
        }
    }
}

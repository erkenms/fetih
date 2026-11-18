namespace Comet.Game.Tests.Packets
{
    using Comet.Game.Packets;
    using Comet.Shared.Models;
    using Xunit;

    public class MsgConnectTests
    {
        private static readonly TransferAuthArgs Auth = new TransferAuthArgs
        {
            AccountID = 1,
            IPAddress = "10.0.0.5"
        };

        [Theory]
        [InlineData("10.0.0.5", false, true)]
        [InlineData("10.0.0.9", false, true)]
        [InlineData("10.0.0.5", true, true)]   // strict: same IP is accepted
        [InlineData("10.0.0.9", true, false)]  // strict: different IP is rejected
        public void IsAuthorized(string ipAddress, bool strict, bool expected)
        {
            Assert.Equal(expected, MsgConnect.IsAuthorized(Auth, ipAddress, strict));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingTokenIsNeverAuthorized(bool strict)
        {
            Assert.False(MsgConnect.IsAuthorized(null, "10.0.0.5", strict));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(101, 5)]
        [InlineData(1111, 15)]
        [InlineData(123, 123)]  // not binary digits: raw value, no exception
        [InlineData(-1, -1)]    // negative: raw value, no exception
        [InlineData(int.MaxValue, int.MaxValue)]
        public void ParseVersion(int raw, int expected)
        {
            Assert.Equal(expected, MsgConnect.ParseVersion(raw));
        }
    }
}

namespace Comet.Game.Tests.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Xunit;
    using static Comet.Game.Packets.MsgInteract;

    public class MsgInteractTests
    {
        [Theory]
        [InlineData(16u)]
        [InlineData(19u)]
        [InlineData(23u)]
        [InlineData(30u)]
        [InlineData(33u)]
        [InlineData(34u)]
        [InlineData(38u)]
        public async Task AntiCheatReportsAreOnlyLogged(uint action)
        {
            var packet = new MsgInteract((InteractionType)action, 1000000, 0, 0, 0, 14);
            var decoded = new MsgInteract();
            decoded.Decode(packet.Encode());
            Assert.Equal((InteractionType)action, decoded.Action);
            Assert.Equal(14u, decoded.Data);

            // Completes without touching the client; one routed to Attacks would throw on null
            await decoded.ProcessAsync(null);
        }
    }
}

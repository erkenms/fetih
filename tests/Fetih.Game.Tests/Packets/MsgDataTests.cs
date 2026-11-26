namespace Fetih.Game.Tests.Packets
{
    using System;
    using Fetih.Game.Packets;
    using Xunit;

    public class MsgDataTests
    {
        [Fact]
        public void ServerTimeHasTmFieldsFrom8()
        {
            var bytes = MsgData.ServerTime(new DateTime(2026, 11, 22, 17, 45, 9)).Encode();
            Assert.Equal(36, bytes.Length);
            Assert.Equal(1033, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 4));
            var values = new uint[7];
            for (int i = 0; i < 7; i++)
                values[i] = BitConverter.ToUInt32(bytes, 8 + 4 * i);
            Assert.Equal(new uint[] { 126, 10, 325, 22, 17, 45, 9 }, values);
        }

        [Fact]
        public void VigorHasValueAt8()
        {
            var bytes = MsgData.Vigor(150).Encode();
            Assert.Equal(36, bytes.Length);
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(150u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(0u, BitConverter.ToUInt32(bytes, 12));
        }
    }
}

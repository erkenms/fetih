namespace Comet.Game.Tests.Packets
{
    using System;
    using Comet.Game.Packets;
    using Xunit;

    /// <summary>
    /// Checks the 5619 sizes of MsgAction (38 bytes) and MsgItem (88 bytes), which the
    /// server echoes back to the client during login and for pings.
    /// </summary>
    public class MsgActionItemTests
    {
        [Fact]
        public void MsgActionRoundTripsClientLayout()
        {
            var bytes = new byte[38 + 8];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)38);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)10010);
            BitConverter.TryWriteBytes(bytes.AsSpan(4), 1000001u);
            BitConverter.TryWriteBytes(bytes.AsSpan(16), 123456u);
            BitConverter.TryWriteBytes(bytes.AsSpan(20), (ushort)132);
            BitConverter.TryWriteBytes(bytes.AsSpan(24), (ushort)61);
            BitConverter.TryWriteBytes(bytes.AsSpan(26), (ushort)109);
            BitConverter.TryWriteBytes(bytes.AsSpan(36), (ushort)7);

            var msg = new MsgAction();
            msg.Decode(bytes);
            Assert.Equal(MsgAction.ActionType.LoginComplete, msg.Action);
            Assert.Equal(61, msg.X);
            Assert.Equal(109, msg.Y);
            Assert.Equal(7, msg.Extra);

            var encoded = msg.Encode();
            Assert.Equal(38, encoded.Length);
            Assert.Equal(bytes.AsSpan(2, 36).ToArray(), encoded.AsSpan(2).ToArray());
        }

        [Fact]
        public void MsgItemEchoIsClientSize()
        {
            var bytes = new byte[MsgItem.Size + 8];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)MsgItem.Size);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)1009);
            BitConverter.TryWriteBytes(bytes.AsSpan(4), 1000001u);
            BitConverter.TryWriteBytes(bytes.AsSpan(12), 27u);
            BitConverter.TryWriteBytes(bytes.AsSpan(16), 99u);

            var msg = new MsgItem();
            msg.Decode(bytes);
            Assert.Equal(MsgItem.ItemActionType.ClientPing, msg.Action);

            var encoded = msg.Encode();
            Assert.Equal(MsgItem.Size, encoded.Length);
            Assert.Equal(99u, BitConverter.ToUInt32(encoded, 16));
        }
    }
}

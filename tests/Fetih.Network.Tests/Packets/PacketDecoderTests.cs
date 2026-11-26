namespace Fetih.Network.Tests.Packets;

using System;
using System.Collections.Generic;
using Fetih.Network.Packets;
using Xunit;

public class PacketDecoderTests
{
    private enum SampleAction : ushort
    {
        Ping = 7,
    }

    private sealed class SampleClient;

    private sealed class MsgAction : MsgBase<SampleClient>
    {
        public uint Identity { get; set; }
        public SampleAction Action { get; set; }
        public List<uint> Arguments { get; set; } = new();

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
            this.Action = (SampleAction)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgAction);
            writer.Write(this.Identity);
            writer.Write((ushort)this.Action);
            return writer.ToArray();
        }
    }

    [Fact]
    public void DecoderAndFieldsDescribePacketWithoutHeader()
    {
        var packet = new MsgAction
        {
            Identity = 1000001,
            Action = SampleAction.Ping,
            Arguments = new List<uint> { 10, 20 },
        };
        byte[] bytes = packet.Encode();
        BitConverter.TryWriteBytes(bytes, (ushort)bytes.Length);

        var decoder = PacketDecoder.For<SampleClient>();
        var decoded = Assert.IsType<MsgAction>(decoder.Decode(bytes));
        Assert.Equal(1000001u, decoded.Identity);
        Assert.Equal(SampleAction.Ping, decoded.Action);

        var log = PacketFields.ToLogObject(packet, new HashSet<string> { "Length", "Type" });
        Assert.DoesNotContain("Length", log.Keys);
        Assert.DoesNotContain("Type", log.Keys);
        Assert.Equal("1000001", log["Identity"]);
        Assert.Equal("Ping (7)", log["Action"]);
        var args = Assert.IsAssignableFrom<List<object>>(log["Arguments"]);
        Assert.Equal(new object[] { "10", "20" }, args);
    }

    [Fact]
    public void DecoderReturnsNullForShortOrUnknownPackets()
    {
        var decoder = PacketDecoder.For<SampleClient>();
        Assert.Null(decoder.Decode(new byte[] { 1, 2 }));
        Assert.Null(decoder.Decode(new byte[] { 0x08, 0x00, 0xFF, 0xFF, 0, 0, 0, 0 }));
    }
}

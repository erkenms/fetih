namespace Fetih.Game.Packets
{
    using System.Net;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Sent by the 5619 client every 30 minutes once logged in, 20 bytes: action byte at 4
    // (always 0), the client's local IPv4 address u32 at 8, the round trip in milliseconds it
    // last measured with the MsgItem ping (the status line's "Ping") u32 at 12, and its own
    // millisecond clock u32 at 16. The client has no handler for it coming back, so it is
    // only logged.
    public sealed class MsgPing : MsgBase<Client>
    {
        public byte Action { get; set; }
        public uint LocalAddress { get; set; }
        public uint Latency { get; set; }
        public uint ClientTime { get; set; }

        public MsgPing() => base.Type = PacketType.MsgPing;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = reader.ReadByte();
            reader.ReadBytes(3);
            this.LocalAddress = reader.ReadUInt32();
            this.Latency = reader.ReadUInt32();
            this.ClientTime = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Action);          // 4
            writer.Write(new byte[3]);
            writer.Write(this.LocalAddress);    // 8
            writer.Write(this.Latency);         // 12
            writer.Write(this.ClientTime);      // 16
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client)
        {
            LogEvents.For<MsgPing>(LogEvents.SessionPing).Information(
                "Ping from {LocalAddress}, latency {Latency} ms",
                new IPAddress(this.LocalAddress).ToString(), this.Latency);
            return Task.CompletedTask;
        }
    }
}

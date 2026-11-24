namespace Comet.Network.Sockets
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using Comet.Network.Packets;

    // Packets per type, for whichever exporter the server attaches to this meter
    public static class PacketMetrics
    {
        public const string MeterName = "Comet.Network";
        public const string ReceivedName = "comet.packets.received";
        public const string SentName = "comet.packets.sent";

        private static readonly Meter Meter = new Meter(MeterName);
        private static readonly Counter<long> Received = Meter.CreateCounter<long>(
            ReceivedName, description: "Packets received from clients");
        private static readonly Counter<long> Sent = Meter.CreateCounter<long>(
            SentName, description: "Packets sent to clients");
        private static readonly KeyValuePair<string, object>?[] Tags =
            new KeyValuePair<string, object>?[ushort.MaxValue + 1];

        public static void In(ReadOnlySpan<byte> packet)
        {
            if (Received.Enabled)
                Received.Add(1, TagOf(packet));
        }

        public static void Out(ReadOnlySpan<byte> packet)
        {
            if (Sent.Enabled)
                Sent.Add(1, TagOf(packet));
        }

        private static KeyValuePair<string, object> TagOf(ReadOnlySpan<byte> packet)
        {
            ushort type = packet.Length >= 4 ? BitConverter.ToUInt16(packet[2..]) : (ushort)0;
            return Tags[type] ??= new KeyValuePair<string, object>("type",
                Enum.IsDefined(typeof(PacketType), type) ? ((PacketType)type).ToString() : type.ToString());
        }
    }
}

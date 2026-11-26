namespace Fetih.Network.Sockets.Tests
{
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using Xunit;

    public class PacketMetricsTests
    {
        [Fact]
        public void CountsPacketsByTypeName()
        {
            var seen = new List<(string Instrument, long Value, string Type)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PacketMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                lock (seen) seen.Add((instrument.Name, value, (string)tags[0].Value));
            });
            listener.Start();

            // MsgTalk (1004) in, an unknown type 60000 out
            PacketMetrics.In(new byte[] { 4, 0, 0xEC, 0x03 });
            PacketMetrics.Out(new byte[] { 4, 0, 0x60, 0xEA });

            lock (seen)
            {
                Assert.Contains((PacketMetrics.ReceivedName, 1L, "MsgTalk"), seen);
                Assert.Contains((PacketMetrics.SentName, 1L, "60000"), seen);
            }
        }
    }
}

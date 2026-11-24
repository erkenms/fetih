namespace Comet.Game
{
    using System;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using Comet.Network.Sockets;
    using Comet.Core.Logging;
    using Prometheus;
    using Serilog;

    // Prometheus metrics on http://<IPAddress>:<Port>/metrics. Gauges are read from the
    // kernel when scraped; packet counters come from the Comet.Network meter.
    public static class GameMetrics
    {
        private static readonly Gauge Online = Metrics.CreateGauge(
            "comet_online_players", "Characters logged in");
        private static readonly Gauge MapsLoaded = Metrics.CreateGauge(
            "comet_maps_loaded", "Maps loaded into the world, out of comet_maps_known");
        private static readonly Gauge MapsKnown = Metrics.CreateGauge(
            "comet_maps_known", "Maps the client has");
        private static readonly Gauge MonstersAlive = Metrics.CreateGauge(
            "comet_monsters_alive", "Monsters alive on loaded maps");
        private static readonly Gauge GroundItems = Metrics.CreateGauge(
            "comet_ground_items", "Items lying on the ground");
        private static readonly Histogram SaveRound = Metrics.CreateHistogram(
            "comet_save_round_seconds", "Time to save every online character in one periodic round",
            new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.025, 2, 11) });
        private static readonly Counter PacketsIn = Metrics.CreateCounter(
            "comet_packets_received_total", "Packets received from clients", "type");
        private static readonly Counter PacketsOut = Metrics.CreateCounter(
            "comet_packets_sent_total", "Packets sent to clients", "type");
        private static MeterListener packetListener;
        private static readonly Gauge SaveRoundFailures = Metrics.CreateGauge(
            "comet_save_round_failed_characters", "Characters the last periodic round couldn't save");

        public static void Start(MetricsConfiguration config)
        {
            if (config == null || config.Port <= 0)
                return;

            Metrics.SuppressDefaultMetrics(new SuppressDefaultMetricOptions { SuppressMeters = true });
            Metrics.DefaultRegistry.AddBeforeCollectCallback(Collect);
            ListenToPackets();
            try
            {
                new KestrelMetricServer(config.IPAddress, config.Port).Start();
                LogEvents.For(typeof(GameMetrics), LogEvents.MetricsListening).Information(
                    "Serving metrics on http://{IPAddress}:{Port}/metrics", config.IPAddress, config.Port);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Can't serve metrics on {IPAddress}:{Port}", config.IPAddress, config.Port);
            }
        }

        public static void SaveRoundDone(TimeSpan duration, int saved, int online)
        {
            SaveRound.Observe(duration.TotalSeconds);
            SaveRoundFailures.Set(online - saved);
        }

        private static void ListenToPackets()
        {
            packetListener = new MeterListener();
            packetListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PacketMetrics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            packetListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var counter = instrument.Name == PacketMetrics.ReceivedName ? PacketsIn : PacketsOut;
                counter.WithLabels(tags.Length > 0 ? (string)tags[0].Value : "").Inc(value);
            });
            packetListener.Start();
        }

        private static void Collect()
        {
            Online.Set(Kernel.Players.Count);
            if (Kernel.Maps != null)
                MapsKnown.Set(Kernel.Maps.Count);
            if (Kernel.World != null && Kernel.Monsters != null)
                MapsLoaded.Set(Kernel.World.Loaded.Select(m => m.Id)
                    .Union(Kernel.Monsters.Loaded.Select(m => m.Id)).Count());
            if (Kernel.Monsters != null)
                MonstersAlive.Set(Kernel.Monsters.Loaded.Sum(m => m.All().Count(x => x.Alive)));
            if (Kernel.Ground != null)
                GroundItems.Set(Kernel.Ground.Loaded.Sum(m => m.Count));
        }
    }

    public class MetricsConfiguration
    {
        // Keep it on loopback: only Prometheus on this machine should read it. 0 turns it off.
        public string IPAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9818;
    }
}

namespace Fetih.Account
{
    using System;
    using Fetih.Core.Logging;
    using Prometheus;
    using Serilog;

    // Prometheus metrics on http://<IPAddress>:<Port>/metrics. Lets Prometheus track
    // whether the account server is up and accepting scrapes.
    public static class AccountMetrics
    {
        public static void Start(MetricsConfiguration config)
        {
            if (config == null || config.Port <= 0)
                return;

            Metrics.SuppressDefaultMetrics(new SuppressDefaultMetricOptions { SuppressMeters = true });
            try
            {
                new KestrelMetricServer(config.IPAddress, config.Port).Start();
                LogEvents.For(typeof(AccountMetrics), LogEvents.MetricsListening).Information(
                    "Serving metrics on http://{IPAddress}:{Port}/metrics", config.IPAddress, config.Port);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Can't serve metrics on {IPAddress}:{Port}", config.IPAddress, config.Port);
            }
        }
    }

    public class MetricsConfiguration
    {
        // Keep it on loopback: only Prometheus on this machine should read it. 0 turns it off.
        public string IPAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9819;
    }
}

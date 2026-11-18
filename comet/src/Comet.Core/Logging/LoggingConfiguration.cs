namespace Comet.Core.Logging
{
    using System.Collections.Generic;

    /// <summary>
    /// "Logging" section of a server configuration file. Every value can be overridden
    /// from the command line, e.g. <c>--Logging:Level=trace</c>.
    /// </summary>
    public class LoggingConfiguration
    {
        /// <summary>Minimum level: trace, debug, info, warn or error.</summary>
        public string Level { get; set; } = "info";

        /// <summary>
        /// Per-source minimum levels, keyed by namespace or class name prefix, e.g.
        /// <c>"Comet.Network": "debug"</c>.
        /// </summary>
        public Dictionary<string, string> Overrides { get; set; } = new Dictionary<string, string>();

        /// <summary>Directory for JSON log files, relative to the working directory.</summary>
        public string Directory { get; set; } = "logs";

        /// <summary>Number of daily log files to keep before deleting the oldest.</summary>
        public int RetainDays { get; set; } = 30;

        /// <summary>
        /// Which connections get every packet logged (packet.in and packet.out, trace
        /// level, JSON files only). Tracing works at any <see cref="Level"/>.
        /// </summary>
        public PacketTraceConfiguration PacketTrace { get; set; } = new PacketTraceConfiguration();
    }

    /// <summary>
    /// "Logging:PacketTrace" section: connections whose packets are logged. A connection
    /// is traced if any of these match it.
    /// </summary>
    public class PacketTraceConfiguration
    {
        /// <summary>Trace every connection.</summary>
        public bool All { get; set; }

        /// <summary>Account IDs to trace (from the moment the account is known).</summary>
        public List<uint> Accounts { get; set; } = new List<uint>();

        /// <summary>Client IP addresses to trace, e.g. a test VM.</summary>
        public List<string> IPs { get; set; } = new List<string>();
    }
}

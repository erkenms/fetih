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
    }
}

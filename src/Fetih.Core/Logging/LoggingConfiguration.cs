namespace Fetih.Core.Logging
{
    using System.Collections.Generic;

    public class LoggingConfiguration
    {
        public string Level { get; set; } = "info";

        public string ConsoleLevel { get; set; }

        public Dictionary<string, string> Overrides { get; set; } = new Dictionary<string, string>();

        public string Directory { get; set; } = "logs";

        public int RetainDays { get; set; } = 30;

        public bool AuditEnabled { get; set; } = true;

        public string AuditDirectory { get; set; }

        public PacketTraceConfiguration PacketTrace { get; set; } = new PacketTraceConfiguration();
    }

    public class PacketTraceConfiguration
    {
        public bool All { get; set; }

        public List<uint> Accounts { get; set; } = new List<uint>();

        public List<string> IPs { get; set; } = new List<string>();
    }
}

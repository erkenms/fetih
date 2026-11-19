namespace Comet.Core.Logging
{
    using System;
    using Serilog.Events;

    public static class LogLevels
    {
        public static string Name(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => "trace",
            LogEventLevel.Debug => "debug",
            LogEventLevel.Information => "info",
            LogEventLevel.Warning => "warn",
            _ => "error" // Error and Fatal: Comet only uses five levels
        };

        public static string Tag(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => "TRC",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            _ => "ERR"
        };

        public static bool TryParse(string name, out LogEventLevel level)
        {
            switch (name?.Trim().ToLowerInvariant())
            {
                case "trace": case "verbose": level = LogEventLevel.Verbose; return true;
                case "debug": level = LogEventLevel.Debug; return true;
                case "info": case "information": level = LogEventLevel.Information; return true;
                case "warn": case "warning": level = LogEventLevel.Warning; return true;
                case "error": level = LogEventLevel.Error; return true;
                default: level = LogEventLevel.Information; return false;
            }
        }

        public static LogEventLevel Parse(string name) =>
            TryParse(name, out var level) ? level
                : throw new ArgumentException($"Unknown log level '{name}'. " +
                    "Use trace, debug, info, warn or error.", nameof(name));
    }
}

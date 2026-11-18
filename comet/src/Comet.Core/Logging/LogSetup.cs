namespace Comet.Core.Logging
{
    using System;
    using System.IO;
    using Serilog;
    using Serilog.Core;
    using Serilog.Events;

    /// <summary>
    /// Configures the global Serilog logger for a server process: readable console
    /// output plus daily rolling JSON Lines files at <c>{Directory}/{server}-yyyyMMdd.jsonl</c>.
    /// </summary>
    public static class LogSetup
    {
        /// <summary>
        /// Controls the minimum level at runtime (e.g. for a future /loglevel command).
        /// </summary>
        public static LoggingLevelSwitch Level { get; } = new LoggingLevelSwitch();

        /// <summary>
        /// Source of packet trace events (Comet.Network.Sockets.PacketTrace). They are
        /// written at trace level whatever the configured level, and only to the files.
        /// </summary>
        public const string PacketTraceSource = "Comet.Network.Sockets.PacketTrace";

        /// <summary>
        /// Creates and installs the global logger.
        /// </summary>
        /// <param name="server">Short server name written to every line ("account", "game")</param>
        /// <param name="config">Logging configuration; defaults are used if null</param>
        public static void Configure(string server, LoggingConfiguration config)
        {
            config ??= new LoggingConfiguration();
            Level.MinimumLevel = LogLevels.Parse(config.Level);

            var logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(Level)
                .Enrich.WithProperty("Server", server)
                .Enrich.FromLogContext();

            foreach (var entry in config.Overrides)
                logger.MinimumLevel.Override(entry.Key, LogLevels.Parse(entry.Value));

            // Which connections are traced is decided by PacketTrace itself
            logger.MinimumLevel.Override(PacketTraceSource, LogEventLevel.Verbose);

            logger.WriteTo.Logger(console => console
                .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(PacketTraceSource))
                .WriteTo.Console(new ConsoleLogFormatter(colors: !Console.IsOutputRedirected)));
            logger.WriteTo.File(
                new JsonLogFormatter(),
                Path.Combine(config.Directory, server + "-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: config.RetainDays > 0 ? config.RetainDays : null);

            Log.Logger = logger.CreateLogger();
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                LogEvents.For(typeof(LogSetup), LogEvents.ServerStop).Information("Server stopping");
                Log.CloseAndFlush();
            };
        }
    }
}

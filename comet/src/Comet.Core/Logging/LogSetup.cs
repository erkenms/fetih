namespace Comet.Core.Logging
{
    using System;
    using System.IO;
    using Serilog;
    using Serilog.Core;
    using Serilog.Events;

    public static class LogSetup
    {
        public static LoggingLevelSwitch Level { get; } = new LoggingLevelSwitch();

        public const string PacketTraceSource = "Comet.Network.Sockets.PacketTrace";

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

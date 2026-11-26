namespace Fetih.Core.Logging
{
    using System;
    using System.IO;
    using Serilog;
    using Serilog.Core;
    using Serilog.Events;

    public static class LogSetup
    {
        public static LoggingLevelSwitch Level { get; } = new LoggingLevelSwitch();

        public const string PacketTraceSource = "Fetih.Network.Sockets.PacketTrace";

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

            var consoleLevel = string.IsNullOrWhiteSpace(config.ConsoleLevel)
                ? LevelAlias.Minimum
                : LogLevels.Parse(config.ConsoleLevel);

            logger.WriteTo.Logger(console => console
                .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(PacketTraceSource))
                .WriteTo.Console(
                    new ConsoleLogFormatter(colors: !Console.IsOutputRedirected),
                    restrictedToMinimumLevel: consoleLevel));
            logger.WriteTo.File(
                new JsonLogFormatter(),
                Path.Combine(config.Directory, server + "-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: config.RetainDays > 0 ? config.RetainDays : null);

            if (config.AuditEnabled)
            {
                var auditDir = string.IsNullOrWhiteSpace(config.AuditDirectory)
                    ? Path.Combine(config.Directory, "audit")
                    : config.AuditDirectory;
                var auditPrefix = server + "-audit-";
                logger.WriteTo.Logger(audit => audit
                    .Filter.ByIncludingOnly(AuditEvents.IsAudited)
                    .WriteTo.File(
                        new JsonLogFormatter(),
                        Path.Combine(auditDir, auditPrefix + ".jsonl"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: null,
                        hooks: new AuditCompressHook(auditPrefix)));
            }

            Log.Logger = logger.CreateLogger();
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                LogEvents.For(typeof(LogSetup), LogEvents.ServerStop).Information("Server stopping");
                Log.CloseAndFlush();
            };
        }
    }
}

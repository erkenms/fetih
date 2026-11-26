namespace Fetih.Core.Logging
{
    using System;
    using System.Globalization;
    using System.IO;
    using Serilog.Events;
    using Serilog.Formatting;

    public sealed class ConsoleLogFormatter : ITextFormatter
    {
        private readonly bool Colors;

        public ConsoleLogFormatter(bool colors) => this.Colors = colors;

        public void Format(LogEvent logEvent, TextWriter output)
        {
            output.Write(logEvent.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            output.Write(' ');

            if (this.Colors) output.Write(Color(logEvent.Level));
            output.Write(LogLevels.Tag(logEvent.Level));
            if (this.Colors) output.Write("\u001b[0m");
            output.Write(' ');

            if (logEvent.Properties.TryGetValue("Event", out var name) &&
                name is ScalarValue { Value: string eventName })
            {
                output.Write(eventName.PadRight(16));
                output.Write(' ');
            }

            output.Write(JsonLogFormatter.RenderMessage(logEvent));
            output.Write(Environment.NewLine);

            if (logEvent.Exception != null)
            {
                output.Write(logEvent.Exception.ToString());
                output.Write(Environment.NewLine);
            }
        }

        private static string Color(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => "\u001b[90m",
            LogEventLevel.Debug => "\u001b[37m",
            LogEventLevel.Information => "\u001b[32m",
            LogEventLevel.Warning => "\u001b[33m",
            _ => "\u001b[31m"
        };
    }
}

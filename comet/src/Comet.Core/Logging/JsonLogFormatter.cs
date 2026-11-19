namespace Comet.Core.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Serilog.Events;
    using Serilog.Formatting;
    using Serilog.Formatting.Json;
    using Serilog.Parsing;

    public sealed class JsonLogFormatter : ITextFormatter
    {
        public const int SchemaVersion = 1;

        // Properties that map onto reserved top-level fields
        private const string ServerProperty = "Server";
        private const string EventProperty = "Event";
        private const string SourceProperty = "SourceContext";

        private static readonly HashSet<string> Reserved = new HashSet<string>
        {
            "v", "ts", "level", "server", "event", "msg", "src", "exc"
        };

        private readonly JsonValueFormatter ValueFormatter = new JsonValueFormatter(typeTagName: null);

        public void Format(LogEvent logEvent, TextWriter output)
        {
            output.Write("{\"v\":");
            output.Write(SchemaVersion.ToString(CultureInfo.InvariantCulture));

            output.Write(",\"ts\":\"");
            output.Write(logEvent.Timestamp.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            output.Write('"');

            output.Write(",\"level\":\"");
            output.Write(LogLevels.Name(logEvent.Level));
            output.Write('"');

            this.WriteScalar(logEvent, ServerProperty, "server", output);
            this.WriteScalar(logEvent, EventProperty, "event", output);

            output.Write(",\"msg\":");
            JsonValueFormatter.WriteQuotedJsonString(RenderMessage(logEvent), output);

            foreach (var property in logEvent.Properties)
            {
                if (property.Key == ServerProperty || property.Key == EventProperty ||
                    property.Key == SourceProperty)
                    continue;

                var name = CamelCase(property.Key);
                if (Reserved.Contains(name))
                    name = "_" + name;

                output.Write(',');
                JsonValueFormatter.WriteQuotedJsonString(name, output);
                output.Write(':');
                this.ValueFormatter.Format(property.Value, output);
            }

            this.WriteScalar(logEvent, SourceProperty, "src", output);

            if (logEvent.Exception != null)
            {
                output.Write(",\"exc\":");
                JsonValueFormatter.WriteQuotedJsonString(logEvent.Exception.ToString(), output);
            }

            output.Write('}');
            output.Write('\n');
        }

        private void WriteScalar(LogEvent logEvent, string property, string field, TextWriter output)
        {
            if (!logEvent.Properties.TryGetValue(property, out var value))
                return;

            output.Write(",\"");
            output.Write(field);
            output.Write("\":");
            this.ValueFormatter.Format(value, output);
        }

        public static string RenderMessage(LogEvent logEvent)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            foreach (var token in logEvent.MessageTemplate.Tokens)
            {
                if (token is PropertyToken property &&
                    logEvent.Properties.TryGetValue(property.PropertyName, out var value) &&
                    value is ScalarValue { Value: string text })
                {
                    writer.Write(text);
                }
                else
                {
                    token.Render(logEvent.Properties, writer, CultureInfo.InvariantCulture);
                }
            }

            return writer.ToString();
        }

        public static string CamelCase(string name)
        {
            if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
                return name;

            // Lower the leading run of capitals, keeping the last one of an acronym
            // followed by lowercase as the start of the next word (IPAddress → ipAddress).
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length && char.IsUpper(chars[i]); i++)
            {
                if (i > 0 && i + 1 < chars.Length && char.IsLower(chars[i + 1]))
                    break;
                chars[i] = char.ToLowerInvariant(chars[i]);
            }

            return new string(chars);
        }
    }
}

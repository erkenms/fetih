namespace Fetih.Core.Tests.Logging
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Fetih.Core.Logging;
    using Serilog;
    using Serilog.Core;
    using Serilog.Events;
    using Xunit;

    public class JsonLogFormatterTests
    {
        private sealed class CollectingSink : ILogEventSink
        {
            public readonly List<LogEvent> Events = new List<LogEvent>();
            public void Emit(LogEvent logEvent) => this.Events.Add(logEvent);
        }

        private static (string Line, JsonElement Json) Capture(Action<ILogger> write)
        {
            var sink = new CollectingSink();
            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithProperty("Server", "game")
                .WriteTo.Sink(sink)
                .CreateLogger();
            write(logger);

            var output = new StringWriter();
            new JsonLogFormatter().Format(sink.Events.Single(), output);
            var line = output.ToString();
            return (line, JsonDocument.Parse(line).RootElement);
        }

        [Fact]
        public void WritesOneLineWithStableFieldOrder()
        {
            var (line, json) = Capture(log => log
                .ForContext<JsonLogFormatterTests>()
                .ForContext("Event", "session.login")
                .Information("{Name} logged in from {IPAddress}", "Bob", "10.0.0.5"));

            Assert.EndsWith("\n", line);
            Assert.Equal(1, line.Count(c => c == '\n'));

            var names = json.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "v", "ts", "level", "server", "event", "msg", "name", "ipAddress", "src" }, names);
            Assert.Equal(1, json.GetProperty("v").GetInt32());
            Assert.Equal("info", json.GetProperty("level").GetString());
            Assert.Equal("game", json.GetProperty("server").GetString());
            Assert.Equal("session.login", json.GetProperty("event").GetString());
            Assert.Equal("Bob logged in from 10.0.0.5", json.GetProperty("msg").GetString());
            Assert.Equal(typeof(JsonLogFormatterTests).FullName, json.GetProperty("src").GetString());
        }

        [Fact]
        public void TimestampIsUtcWithMilliseconds()
        {
            var (_, json) = Capture(log => log.Information("x"));
            var ts = json.GetProperty("ts").GetString();
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", ts);
        }

        [Fact]
        public void KeepsValueTypes()
        {
            var (_, json) = Capture(log => log.Information(
                "{CharacterId} {DurationSec} {Online}", 1000001u, 12.5, true));

            Assert.Equal(JsonValueKind.Number, json.GetProperty("characterId").ValueKind);
            Assert.Equal(1000001u, json.GetProperty("characterId").GetUInt32());
            Assert.Equal(12.5, json.GetProperty("durationSec").GetDouble());
            Assert.True(json.GetProperty("online").GetBoolean());
        }

        [Fact]
        public void EventIsOmittedWhenNotSet()
        {
            var (_, json) = Capture(log => log.Warning("plain"));
            Assert.False(json.TryGetProperty("event", out _));
            Assert.Equal("warn", json.GetProperty("level").GetString());
        }

        [Fact]
        public void ExceptionIsSingleEscapedString()
        {
            var (line, json) = Capture(log => log.Error(
                new InvalidOperationException("boom\nsecond line"), "failed"));

            Assert.Equal(1, line.Count(c => c == '\n'));
            Assert.Contains("boom\nsecond line", json.GetProperty("exc").GetString());
            Assert.Equal("error", json.GetProperty("level").GetString());
        }

        [Fact]
        public void PropertiesCollidingWithReservedFieldsArePrefixed()
        {
            var (_, json) = Capture(log => log.Information("{Msg} {Level}", "a", "b"));
            Assert.Equal("a", json.GetProperty("_msg").GetString());
            Assert.Equal("b", json.GetProperty("_level").GetString());
            Assert.Equal("a b", json.GetProperty("msg").GetString());
        }

        [Theory]
        [InlineData("CharacterId", "characterId")]
        [InlineData("IPAddress", "ipAddress")]
        [InlineData("IP", "ip")]
        [InlineData("X", "x")]
        [InlineData("mapId", "mapId")]
        public void CamelCase(string input, string expected)
        {
            Assert.Equal(expected, JsonLogFormatter.CamelCase(input));
        }

        [Theory]
        [InlineData("trace", LogEventLevel.Verbose)]
        [InlineData("DEBUG", LogEventLevel.Debug)]
        [InlineData("info", LogEventLevel.Information)]
        [InlineData("Information", LogEventLevel.Information)]
        [InlineData("warn", LogEventLevel.Warning)]
        [InlineData("error", LogEventLevel.Error)]
        public void ParsesLevelNames(string name, LogEventLevel expected)
        {
            Assert.Equal(expected, LogLevels.Parse(name));
        }

        [Fact]
        public void RejectsUnknownLevel()
        {
            Assert.Throws<ArgumentException>(() => LogLevels.Parse("loud"));
        }

        [Theory]
        [InlineData(LogEventLevel.Verbose, "trace")]
        [InlineData(LogEventLevel.Debug, "debug")]
        [InlineData(LogEventLevel.Information, "info")]
        [InlineData(LogEventLevel.Warning, "warn")]
        [InlineData(LogEventLevel.Error, "error")]
        [InlineData(LogEventLevel.Fatal, "error")]
        public void WritesLevelNames(LogEventLevel level, string expected)
        {
            Assert.Equal(expected, LogLevels.Name(level));
        }
    }
}

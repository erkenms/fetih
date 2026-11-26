namespace Fetih.Core.Logging.Tests
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using Serilog;
    using Xunit;

    public class AuditLoggingTests
    {
        [Theory]
        [InlineData("account.login")]
        [InlineData("account.login_failed")]
        [InlineData("account.created")]
        [InlineData("game.auth_failed")]
        [InlineData("session.login")]
        [InlineData("session.logout")]
        [InlineData("session.replaced")]
        [InlineData("session.cheat_report")]
        [InlineData("character.deleted")]
        [InlineData("trade.completed")]
        [InlineData("booth.sold")]
        [InlineData("gm.command")]
        [InlineData("gm.ban")]
        [InlineData("gm.unban")]
        [InlineData("gm.give_item")]
        [InlineData("gm.packet_trace")]
        [InlineData("web.login")]
        [InlineData("web.account.ban")]
        [InlineData("web.character.give_item")]
        public void AllowlistIncludesAuditEvents(string eventName)
        {
            Assert.True(AuditEvents.IsAudited(eventName));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("packet.in")]
        [InlineData("packet.out")]
        [InlineData("move.walk")]
        [InlineData("move.jump")]
        [InlineData("combat.hit")]
        [InlineData("monster.killed")]
        [InlineData("character.saved")]
        [InlineData("web.error")]
        [InlineData("web.rpc_connected")]
        [InlineData("web.rpc_lost")]
        public void AllowlistExcludesNonAuditEvents(string eventName)
        {
            Assert.False(AuditEvents.IsAudited(eventName));
        }

        [Fact]
        public void CompressRolledFilesCompressesOlderAuditFilesOnly()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fetih-audit-compress-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var oldFile = Path.Combine(dir, "game-audit-20261124.jsonl");
                var activeFile = Path.Combine(dir, "game-audit-20261125.jsonl");
                var unrelatedFile = Path.Combine(dir, "game-20261124.jsonl");
                const string payload = "{\"event\":\"gm.ban\",\"accountId\":42}\n";

                File.WriteAllText(oldFile, payload);
                File.WriteAllText(activeFile, "{\"event\":\"session.login\"}\n");
                File.WriteAllText(unrelatedFile, "{\"event\":\"move.walk\"}\n");

                AuditCompressHook.CompressRolledFiles(dir, activeFile, "game-audit-");

                Assert.False(File.Exists(oldFile));
                Assert.True(File.Exists(oldFile + ".gz"));
                Assert.True(File.Exists(activeFile));
                Assert.True(File.Exists(unrelatedFile));

                using var fs = File.OpenRead(oldFile + ".gz");
                using var gz = new GZipStream(fs, CompressionMode.Decompress);
                using var reader = new StreamReader(gz);
                Assert.Equal(payload, reader.ReadToEnd());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AuditSinkWritesOnlyAllowlistedEvents()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fetih-audit-sink-" + Guid.NewGuid().ToString("N"));
            var auditDir = Path.Combine(dir, "audit");
            var previousLogger = Log.Logger;
            try
            {
                LogSetup.Configure("game", new LoggingConfiguration
                {
                    Level = "info",
                    ConsoleLevel = "warn",
                    Directory = dir,
                    AuditDirectory = auditDir,
                    RetainDays = 3,
                });

                LogEvents.For<AuditLoggingTests>(LogEvents.SessionLogin).Information("Logged in");
                LogEvents.For<AuditLoggingTests>(LogEvents.MonsterKilled).Information("Killed monster");
                LogEvents.For<AuditLoggingTests>(LogEvents.GmBan).Information("Banned account");
                Log.CloseAndFlush();

                var mainLines = Directory.EnumerateFiles(dir, "game-*.jsonl")
                    .SelectMany(File.ReadAllLines)
                    .ToList();
                var auditLines = Directory.EnumerateFiles(auditDir, "game-audit-*.jsonl")
                    .SelectMany(File.ReadAllLines)
                    .ToList();

                Assert.Contains(mainLines, l => l.Contains("\"event\":\"session.login\""));
                Assert.Contains(mainLines, l => l.Contains("\"event\":\"monster.killed\""));
                Assert.Contains(mainLines, l => l.Contains("\"event\":\"gm.ban\""));

                Assert.Contains(auditLines, l => l.Contains("\"event\":\"session.login\""));
                Assert.Contains(auditLines, l => l.Contains("\"event\":\"gm.ban\""));
                Assert.DoesNotContain(auditLines, l => l.Contains("\"event\":\"monster.killed\""));
            }
            finally
            {
                Log.Logger = previousLogger;
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }
    }
}

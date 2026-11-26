namespace Comet.Integration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Database;
    using MySqlConnector;
    using Xunit;

    // The migrator against a throwaway schema; needs the fetih-db container like the other
    // integration tests
    public sealed class MigrationTests : IAsyncLifetime
    {
        private static string Env(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

        private static readonly string Host = Env("COMET_TEST_DB_HOST", "localhost");
        private static readonly string User = Env("COMET_TEST_DB_USER", "root");
        private static readonly string Password = Env("COMET_TEST_DB_PASSWORD", "password");
        private readonly string Schema = $"comet_test_mig_{Guid.NewGuid():N}"[..28];

        private static readonly Migration Baseline = new(0, "baseline", "CREATE TABLE base_ran (x int);");
        private static readonly Migration AddColumn = new(1, "add-column",
            "ALTER TABLE base_ran ADD COLUMN y int NOT NULL DEFAULT 7;");

        public Task InitializeAsync() => Task.CompletedTask;
        public async Task DisposeAsync() => await Run($"DROP DATABASE IF EXISTS `{this.Schema}`");

        private Task Apply(params Migration[] migrations) =>
            SchemaMigrator.ApplyAsync(Host, this.Schema, User, Password, migrations);

        private async Task<object> Query(string sql)
        {
            await using var db = new MySqlConnection(new MySqlConnectionStringBuilder
                { Server = Host, UserID = User, Password = Password, Database = this.Schema }.ConnectionString);
            await db.OpenAsync();
            return await new MySqlCommand(sql, db).ExecuteScalarAsync();
        }

        private static async Task Run(string sql)
        {
            await using var db = new MySqlConnection(new MySqlConnectionStringBuilder
                { Server = Host, UserID = User, Password = Password }.ConnectionString);
            await db.OpenAsync();
            await new MySqlCommand(sql, db).ExecuteNonQueryAsync();
        }

        [Fact]
        public async Task FreshSchemaGetsBaselineAndMigrations()
        {
            await this.Apply(Baseline, AddColumn);
            Assert.Equal(2L, await this.Query("SELECT COUNT(*) FROM schema_migrations"));
            Assert.Equal(1L, await this.Query(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME = 'base_ran' AND COLUMN_NAME = 'y'"));

            // A second start has nothing to do
            await this.Apply(Baseline, AddColumn);
            Assert.Equal(2L, await this.Query("SELECT COUNT(*) FROM schema_migrations"));
        }

        [Fact]
        public async Task ExistingTablesRecordBaselineWithoutRunningIt()
        {
            await Run($"CREATE DATABASE `{this.Schema}`");
            await Run($"CREATE TABLE `{this.Schema}`.old_table (x int, y int)");
            await this.Apply(Baseline, new Migration(1, "noop", "SELECT 1;"));
            Assert.Equal(0L, await this.Query(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME = 'base_ran'"));
            Assert.Equal(2L, await this.Query("SELECT COUNT(*) FROM schema_migrations"));
        }

        [Fact]
        public async Task ChangedAppliedMigrationStopsStartup()
        {
            await this.Apply(Baseline, AddColumn);
            var e = await Assert.ThrowsAsync<MigrationException>(() =>
                this.Apply(Baseline, AddColumn with { Sql = "ALTER TABLE base_ran ADD COLUMN z int;" }));
            Assert.Contains("0001-add-column", e.Message);
            Assert.Contains("changed", e.Message);
        }

        [Fact]
        public async Task FailedMigrationNamesTheStatementAndStaysUnrecorded()
        {
            var bad = new Migration(1, "bad", "ALTER TABLE base_ran ADD COLUMN ok int;\nINSERT INTO missing_table VALUES (1);");
            var e = await Assert.ThrowsAsync<MigrationException>(() => this.Apply(Baseline, bad));
            Assert.Contains("INSERT INTO missing_table", e.Message);
            Assert.Equal(1L, await this.Query("SELECT COUNT(*) FROM schema_migrations"));
        }
    }
}

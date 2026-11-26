namespace Comet.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Serilog;

    public sealed class MigrationException : Exception
    {
        public MigrationException(string message, Exception inner = null) : base(message, inner) { }
    }

    public sealed record Migration(int Id, string Name, string Sql)
    {
        // Line endings are folded so a checkout with CRLF matches one with LF
        public string Checksum => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(this.Sql.Replace("\r\n", "\n")))).ToLowerInvariant();
    }

    // Brings one schema up to date. Migration 0000 is the baseline (the full schema as it was
    // when migrations began); the rest are comet/sql/migrations/<server>/NNNN-name.sql, applied
    // once each in order. The schema_migrations table remembers what ran and the checksum of
    // the file, so a file edited after it was applied stops the server.
    public static class SchemaMigrator
    {
        private static readonly Regex FileName = new(@"^(\d{4})-(.+)\.sql$", RegexOptions.Compiled);
        private const string Baseline = "sql/baseline.sql";
        private const string MigrationPrefix = "sql/migrations/";

        // The baseline and migrations a server project embeds (see its csproj)
        public static IReadOnlyList<Migration> Load(Assembly assembly)
        {
            string Read(string resource)
            {
                using var stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new MigrationException($"Resource {resource} is missing from {assembly.GetName().Name}");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }

            var list = new List<Migration> { new(0, "baseline", Read(Baseline)) };
            foreach (var resource in assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(MigrationPrefix, StringComparison.Ordinal)))
                list.Add(Parse(resource[MigrationPrefix.Length..], Read(resource)));
            return Order(list);
        }

        public static IReadOnlyList<Migration> LoadFromDirectory(string baselineFile, string migrationsDirectory)
        {
            var list = new List<Migration> { new(0, "baseline", File.ReadAllText(baselineFile)) };
            if (Directory.Exists(migrationsDirectory))
                foreach (var file in Directory.GetFiles(migrationsDirectory, "*.sql"))
                    list.Add(Parse(Path.GetFileName(file), File.ReadAllText(file)));
            return Order(list);
        }

        public static Migration Parse(string fileName, string sql)
        {
            var match = FileName.Match(fileName);
            if (!match.Success || match.Groups[1].Value == "0000")
                throw new MigrationException(
                    $"Migration file '{fileName}' must be named NNNN-name.sql, numbered from 0001");
            return new Migration(int.Parse(match.Groups[1].Value), match.Groups[2].Value, sql);
        }

        private static IReadOnlyList<Migration> Order(List<Migration> list)
        {
            var duplicate = list.GroupBy(m => m.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new MigrationException($"Two migrations are numbered {duplicate.Key:0000}");
            return list.OrderBy(m => m.Id).ToList();
        }

        // Creates the schema if it's missing, then applies what's pending. Throws
        // MigrationException with the file and statement when it can't.
        public static async Task ApplyAsync(
            string hostname, string schema, string username, string password,
            IReadOnlyList<Migration> migrations)
        {
            var connection = new MySqlConnectionStringBuilder
            {
                Server = hostname, UserID = username, Password = password,
                AllowUserVariables = true, Pooling = false
            };

            try
            {
                await using var db = new MySqlConnection(connection.ConnectionString);
                await db.OpenAsync();
                await Execute(db,
                    $"CREATE DATABASE IF NOT EXISTS `{schema}` DEFAULT CHARACTER SET utf8 COLLATE utf8_bin");
                await db.ChangeDatabaseAsync(schema);

                // Held until the connection closes, so two servers don't migrate one schema at once
                var lockName = "fetih-migrate-" + schema;
                if (Convert.ToInt32(await Scalar(db, $"SELECT GET_LOCK('{lockName}', 60)")) != 1)
                    throw new MigrationException($"Another process is migrating {schema}");

                try { await Run(db, schema, migrations); }
                finally { await Scalar(db, $"SELECT RELEASE_LOCK('{lockName}')"); }
            }
            catch (MySqlException e)
            {
                throw new MigrationException($"Migrating {schema}: {e.Message}", e);
            }
        }

        private static async Task Run(MySqlConnection db, string schema, IReadOnlyList<Migration> migrations)
        {
            var tracked = Convert.ToInt32(await Scalar(db,
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME = 'schema_migrations'")) > 0;
            var existing = Convert.ToInt32(await Scalar(db,
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()")) > 0;

            await Execute(db,
                "CREATE TABLE IF NOT EXISTS `schema_migrations` (" +
                "`id` int unsigned NOT NULL, `name` varchar(100) NOT NULL, " +
                "`checksum` char(64) NOT NULL, " +
                "`applied_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY (`id`)" +
                ") ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin");

            var baseline = migrations[0];
            if (!tracked && existing)
            {
                // A database from before migrations: its tables are the baseline
                await Record(db, baseline);
                Log.Information("Schema {Schema} already has tables; recorded baseline migration as applied", schema);
            }

            var applied = new Dictionary<int, string>();
            await using (var reader = await new MySqlCommand("SELECT id, checksum FROM schema_migrations", db)
                .ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    applied[reader.GetInt32(0)] = reader.GetString(1);

            foreach (var migration in migrations)
                if (applied.TryGetValue(migration.Id, out var checksum) && checksum != migration.Checksum)
                    throw new MigrationException(
                        $"Migration {migration.Id:0000}-{migration.Name} was already applied to {schema}, " +
                        "but its file has changed since. Restore the file and add a new migration instead.");

            foreach (var migration in migrations.Where(m => !applied.ContainsKey(m.Id)))
            {
                Log.Information("Applying migration {Id}-{Name} to {Schema}",
                    migration.Id.ToString("0000"), migration.Name, schema);
                await Apply(db, schema, migration);
                await Record(db, migration);
            }
        }

        // MariaDB commits DDL as it goes, so a failure leaves earlier statements applied; the
        // message says which one failed and the migration stays unrecorded.
        private static async Task Apply(MySqlConnection db, string schema, Migration migration)
        {
            foreach (var statement in Split(migration.Sql))
            {
                try { await Execute(db, statement); }
                catch (MySqlException e)
                {
                    throw new MigrationException(
                        $"Migration {migration.Id:0000}-{migration.Name} failed on {schema}: {e.Message}\n" +
                        $"Failed statement: {statement}\n" +
                        "Earlier statements of that migration may already have been applied; " +
                        "fix the schema or the migration, then start the server again.", e);
                }
            }
        }

        // Splits a script at the semicolons outside quotes, backticks and comments. A statement
        // that is only a comment is dropped; "/*! ... */" version comments are statements.
        public static IEnumerable<string> Split(string sql)
        {
            var statement = new StringBuilder();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (c == '\'' || c == '"' || c == '`')
                {
                    int start = i++;
                    while (i < sql.Length && sql[i] != c)
                    {
                        if (sql[i] == '\\' && c != '`') i++;
                        i++;
                    }
                    i = Math.Min(i + 1, sql.Length);
                    statement.Append(sql, start, i - start);
                }
                else if (c == '#' || (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-'
                    && (i + 2 >= sql.Length || char.IsWhiteSpace(sql[i + 2]))))
                {
                    while (i < sql.Length && sql[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? sql.Length : end + 2;
                    if (i + 2 < sql.Length && sql[i + 2] == '!')
                        statement.Append(sql, i, end - i);
                    i = end;
                }
                else if (c == ';')
                {
                    i++;
                    var text = statement.ToString().Trim();
                    if (text.Length > 0) yield return text;
                    statement.Clear();
                }
                else
                {
                    statement.Append(c);
                    i++;
                }
            }
            var last = statement.ToString().Trim();
            if (last.Length > 0) yield return last;
        }

        private static async Task Record(MySqlConnection db, Migration migration)
        {
            var command = new MySqlCommand(
                "INSERT INTO schema_migrations (id, name, checksum) VALUES (@id, @name, @checksum)", db);
            command.Parameters.AddWithValue("@id", migration.Id);
            command.Parameters.AddWithValue("@name", migration.Name);
            command.Parameters.AddWithValue("@checksum", migration.Checksum);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task Execute(MySqlConnection db, string sql) =>
            await new MySqlCommand(sql, db).ExecuteNonQueryAsync();

        private static async Task<object> Scalar(MySqlConnection db, string sql) =>
            await new MySqlCommand(sql, db).ExecuteScalarAsync();
    }
}

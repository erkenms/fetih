namespace Comet.Integration.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Account.Database;
    using Comet.Account.Database.Repositories;
    using Comet.Client;
    using MySqlConnector;
    using Xunit;

    /// <summary>
    /// Runs the real account and game servers for the integration tests. Each test run
    /// creates two new databases in the comet-db container from the default SQL files
    /// (so your own comet.account and comet.game are never touched), starts both servers
    /// as processes on free local ports, and drops the databases afterwards. Server logs
    /// are kept in bin/.../integration-logs until the next run.
    /// </summary>
    public sealed class ServerFixture : IAsyncLifetime
    {
        // Constants
        public const string Realm = "Storm";
        public const string Host = "127.0.0.1";

        // Short, so tests can idle longer than it without being slow
        public const int HandshakeTimeoutSeconds = 2;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        // Database connection (override with environment variables)
        private static readonly string DbHost = Env("COMET_TEST_DB_HOST", "localhost");
        private static readonly string DbUser = Env("COMET_TEST_DB_USER", "root");
        private static readonly string DbPassword = Env("COMET_TEST_DB_PASSWORD", "password");

        // Fields and properties
        private readonly string RunId = Guid.NewGuid().ToString("N")[..8];
        private readonly List<Process> Processes = new List<Process>();
        private int Counter;
        public string AccountSchema { get; }
        public string GameSchema { get; }
        public int AccountPort { get; private set; }
        public int GamePort { get; private set; }
        public string LogDirectory { get; }
        private readonly string RepoRoot;
        private readonly string BuildConfiguration;

        public ServerFixture()
        {
            this.AccountSchema = $"comet_test_{this.RunId}_account";
            this.GameSchema = $"comet_test_{this.RunId}_game";
            this.RepoRoot = FindRepoRoot();

            // Tests run from tests/X/bin/<Configuration>/<framework>/
            var testDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            this.BuildConfiguration = testDir.Parent.Name;
            var logRoot = Path.Combine(AppContext.BaseDirectory, "integration-logs");
            if (Directory.Exists(logRoot))
                Directory.Delete(logRoot, recursive: true);
            this.LogDirectory = Path.Combine(logRoot, this.RunId);
        }

        private static string Env(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is string value && value.Length > 0
                ? value : fallback;

        private static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Comet.slnx")))
                    return dir.FullName;
            throw new InvalidOperationException("Can't find Comet.slnx above the test folder");
        }

        private string ConnectionString(string database = null) =>
            new MySqlConnectionStringBuilder
            {
                Server = DbHost,
                UserID = DbUser,
                Password = DbPassword,
                Database = database ?? "",
                AllowUserVariables = true
            }.ConnectionString;

        public async Task InitializeAsync()
        {
            try
            {
                await using var probe = new MySqlConnection(this.ConnectionString());
                await probe.OpenAsync();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"Integration tests need the comet-db MariaDB container on {DbHost}:3306. " +
                    "Start it from the comet folder with 'podman compose up -d' " +
                    "(or set COMET_TEST_DB_HOST, COMET_TEST_DB_USER, COMET_TEST_DB_PASSWORD).", e);
            }

            // Fresh databases from the default SQL files
            await this.LoadSchemaAsync("comet.account.sql", "comet.account", this.AccountSchema);
            await this.LoadSchemaAsync("comet.game.sql", "comet.game", this.GameSchema);
            this.AccountPort = FreePort();
            this.GamePort = FreePort();
            int rpcPort = FreePort();
            await this.ExecuteAccountSqlAsync(
                "UPDATE realm SET GameIPAddress = @host, RpcIPAddress = @host, GamePort = @game, " +
                "RpcPort = @rpc WHERE Name = @realm",
                ("@host", Host), ("@game", this.GamePort), ("@rpc", rpcPort), ("@realm", Realm));

            // Test accounts are made with the account server's own code
            ServerDbContext.Configuration = new ServerConfiguration.DatabaseConfiguration
            {
                Hostname = DbHost, Schema = this.AccountSchema,
                Username = DbUser, Password = DbPassword
            };

            // The game server first, so the account server finds its RPC port
            this.StartServer("Comet.Game", this.GameSchema, "game",
                "--GameNetwork:IPAddress=" + Host, "--GameNetwork:Port=" + this.GamePort,
                "--GameNetwork:MaxConnPerIP=1000", "--GameNetwork:MaxAttemptsPerMinute=100000",
                "--GameNetwork:HandshakeTimeoutSeconds=" + HandshakeTimeoutSeconds,
                "--RpcNetwork:IPAddress=" + Host, "--RpcNetwork:Port=" + rpcPort);
            await this.WaitForEventAsync("game", "server.start", _ => true, StartTimeout);
            this.StartServer("Comet.Account", this.AccountSchema, "account",
                "--Network:IPAddress=" + Host, "--Network:Port=" + this.AccountPort,
                "--Network:MaxConnPerIP=1000", "--Network:MaxAttemptsPerMinute=100000",
                "--Network:HandshakeTimeoutSeconds=" + HandshakeTimeoutSeconds);
            await this.WaitForEventAsync("account", "server.start", _ => true, StartTimeout);
            await this.WaitForEventAsync("game", "rpc.connected", _ => true, StartTimeout);
        }

        public async Task DisposeAsync()
        {
            foreach (var process in this.Processes)
            {
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                catch (InvalidOperationException) { }
                process.Dispose();
            }

            await using var db = new MySqlConnection(this.ConnectionString());
            await db.OpenAsync();
            foreach (var schema in new[] { this.AccountSchema, this.GameSchema })
                await new MySqlCommand($"DROP DATABASE IF EXISTS `{schema}`", db).ExecuteNonQueryAsync();
        }

        private async Task LoadSchemaAsync(string file, string defaultName, string schema)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(this.RepoRoot, "sql", file));
            sql = sql.Replace($"`{defaultName}`", $"`{schema}`");
            await using var db = new MySqlConnection(this.ConnectionString());
            await db.OpenAsync();
            await new MySqlCommand(sql, db).ExecuteNonQueryAsync();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private string ServerDirectory(string project) => Path.Combine(
            this.RepoRoot, "src", project, "bin", this.BuildConfiguration,
            new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name);

        private string[] DatabaseArgs(string schema) => new[]
        {
            "--Database:Hostname=" + DbHost, "--Database:Schema=" + schema,
            "--Database:Username=" + DbUser, "--Database:Password=" + DbPassword
        };

        private void StartServer(string project, string schema, string server, params string[] args)
        {
            var process = this.StartProcess(project, server, DatabaseArgs(schema)
                .Concat(new[] { "--Logging:Level=debug" }).Concat(args).ToArray());
            this.Processes.Add(process);
        }

        private Process StartProcess(string project, string logName, string[] args)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = this.ServerDirectory(project),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add(project + ".dll");
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            info.ArgumentList.Add("--Logging:Directory=" + Path.Combine(this.LogDirectory, logName));

            var process = Process.Start(info);
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        /// <summary>
        /// Runs the account server's command-line mode (create-account, set-password)
        /// against the test database and returns its exit code.
        /// </summary>
        public async Task<int> RunAccountCommandAsync(params string[] args)
        {
            using var process = this.StartProcess("Comet.Account", "account-cli",
                args.Concat(DatabaseArgs(this.AccountSchema)).ToArray());
            await process.WaitForExitAsync().WaitAsync(StartTimeout);
            return process.ExitCode;
        }

        /// <summary>Runs SQL against the test account database.</summary>
        public async Task ExecuteAccountSqlAsync(string sql, params (string, object)[] parameters)
        {
            await using var db = new MySqlConnection(this.ConnectionString(this.AccountSchema));
            await db.OpenAsync();
            var command = new MySqlCommand(sql, db);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>A new unique account name for this run.</summary>
        public string NewEmail(string prefix = "player") =>
            $"{prefix}{Interlocked.Increment(ref this.Counter)}@test{this.RunId}.example";

        /// <summary>A new unique character name for this run (at most 15 characters).</summary>
        public string NewCharacterName(string prefix = "Hero") =>
            $"{prefix}{Interlocked.Increment(ref this.Counter)}";

        /// <summary>Creates an account through the account server's code.</summary>
        public async Task<string> CreateAccountAsync(string password = "secret1")
        {
            var email = this.NewEmail();
            var (account, error) = await AccountsRepository.CreateAsync(email, password);
            Assert.True(account != null, $"Account not created: {error}");
            return email;
        }

        /// <summary>Connects to the account server.</summary>
        public Task<AccountClient> ConnectAccountAsync() =>
            AccountClient.ConnectAsync(Host, this.AccountPort);

        /// <summary>Connects to the game server and completes the key exchange.</summary>
        public Task<GameClient> ConnectGameAsync() =>
            GameClient.ConnectAsync(Host, this.GamePort);

        /// <summary>Logs in on the account server and returns its answer.</summary>
        public async Task<LoginResult> LoginAsync(string email, string password, string realm = Realm)
        {
            await using var account = await this.ConnectAccountAsync();
            return await account.LoginAsync(email, password, realm);
        }

        /// <summary>Logs in all the way to the map, creating the character if needed.</summary>
        public Task<Player> PlayAsync(string email, string password, NewCharacter character = null) =>
            Player.LoginAsync(Host, this.AccountPort, Realm, email, password, character);

        /// <summary>All log events a server has written so far.</summary>
        public List<JsonElement> Events(string server)
        {
            var events = new List<JsonElement>();
            var dir = Path.Combine(this.LogDirectory, server);
            if (!Directory.Exists(dir))
                return events;
            foreach (var file in Directory.GetFiles(dir, "*.jsonl"))
            {
                using var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    try { events.Add(JsonDocument.Parse(line).RootElement.Clone()); }
                    catch (JsonException) { }
                }
            }
            return events;
        }

        /// <summary>
        /// Waits until a server logs an event that matches, and returns it. Fails the test
        /// if it doesn't appear in time.
        /// </summary>
        public async Task<JsonElement> WaitForEventAsync(
            string server, string eventName, Func<JsonElement, bool> match, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (true)
            {
                foreach (var e in this.Events(server))
                    if (e.TryGetProperty("event", out var name) && name.GetString() == eventName
                        && match(e))
                        return e;
                if (DateTime.UtcNow > deadline)
                    throw new Xunit.Sdk.XunitException(
                        $"The {server} server didn't log {eventName} in time (logs in {this.LogDirectory})");
                await Task.Delay(50);
            }
        }

        /// <summary>Waits for an event whose string field has a value.</summary>
        public Task<JsonElement> WaitForEventAsync(string server, string eventName, string field, string value) =>
            this.WaitForEventAsync(server, eventName,
                e => e.TryGetProperty(field, out var v) && v.ToString() == value);
    }

    /// <summary>All integration tests share one pair of servers.</summary>
    [CollectionDefinition(Name)]
    public sealed class ServerCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "Servers";
    }
}

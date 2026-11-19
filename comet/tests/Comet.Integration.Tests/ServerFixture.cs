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

    public sealed class ServerFixture : IAsyncLifetime
    {
        public const string Realm = "Storm";
        public const string Host = "127.0.0.1";

        // Short, so tests can idle longer than it without being slow, but long enough
        // for a login on a busy machine
        public const int LoginTimeoutSeconds = 5;

        // The one cell of the test Twin City (1002) that can't be walked on
        public static readonly (int X, int Y) BlockedCell = (100, 100);

        // Map 1002's portal to the stable (1006), as in the seeded portal table, and a
        // portal the table has no row for
        public static readonly (ushort X, ushort Y, uint Index) StablePortal = (382, 387, 3);
        public static readonly (ushort X, ushort Y, uint Index) UnlinkedPortal = (700, 700, 99);
        // Item types in the test itemtype.dat
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Blade = (410301, "LuckyBlade", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Potion = (1000000, "Stancher", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Glaive = (510301, "Glaive", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Helmet = (111003, "IronHelmet", 3899, 15);

        // Short, so a test can see a periodic save happen
        public const int SaveIntervalSeconds = 2;

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        // Database connection (override with environment variables)
        private static readonly string DbHost = Env("COMET_TEST_DB_HOST", "localhost");
        private static readonly string DbUser = Env("COMET_TEST_DB_USER", "root");
        private static readonly string DbPassword = Env("COMET_TEST_DB_PASSWORD", "password");

        private readonly string RunId = Guid.NewGuid().ToString("N")[..8];
        private readonly List<Process> Processes = new List<Process>();
        private int Counter;
        public string AccountSchema { get; }
        public string GameSchema { get; }
        public int AccountPort { get; private set; }
        public int GamePort { get; private set; }
        public string LogDirectory { get; }
        public string MapDirectory { get; }
        private readonly Dictionary<string, (long Position, List<JsonElement> Events)> LogFiles =
            new Dictionary<string, (long, List<JsonElement>)>();
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
            this.MapDirectory = Path.Combine(Path.GetTempPath(), "comet-test-maps-" + this.RunId);
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

            // Synthetic map files, so the tests don't need a copy of the client
            this.WriteMaps();

            // The game server first, so the account server finds its RPC port
            this.StartServer("Comet.Game", this.GameSchema, "game",
                "--Maps:ClientDirectory=" + this.MapDirectory,
                "--GameNetwork:IPAddress=" + Host, "--GameNetwork:Port=" + this.GamePort,
                "--GameNetwork:MaxConnPerIP=1000", "--GameNetwork:MaxAttemptsPerMinute=100000",
                "--GameNetwork:LoginTimeoutSeconds=" + LoginTimeoutSeconds,
                "--Saving:IntervalSeconds=" + SaveIntervalSeconds,
                "--RpcNetwork:IPAddress=" + Host, "--RpcNetwork:Port=" + rpcPort);
            await this.WaitForEventAsync("game", "server.start", _ => true, StartTimeout);
            this.StartServer("Comet.Account", this.AccountSchema, "account",
                "--Network:IPAddress=" + Host, "--Network:Port=" + this.AccountPort,
                "--Network:MaxConnPerIP=1000", "--Network:MaxAttemptsPerMinute=100000",
                "--Network:LoginTimeoutSeconds=" + LoginTimeoutSeconds);
            await this.WaitForEventAsync("account", "server.start", _ => true, StartTimeout);
            await this.WaitForEventAsync("game", "rpc.connected", _ => true, StartTimeout);
        }

        private void WriteMaps()
        {
            var maps = Path.Combine(this.MapDirectory, "map", "map");
            Directory.CreateDirectory(maps);
            Directory.CreateDirectory(Path.Combine(this.MapDirectory, "ini"));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "GameMap.dat"),
                MapFiles.GameMapDat((1002, "map/map/newplain.7z"), (1006, "map/map/horse.7z")));
            File.WriteAllBytes(Path.Combine(maps, "newplain.DMap"), MapFiles.DMap(972, 972,
                walkable: (x, y) => (x, y) != BlockedCell,
                portals: new[] { StablePortal, UnlinkedPortal }));
            File.WriteAllBytes(Path.Combine(maps, "horse.DMap"), MapFiles.DMap(52, 52));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "itemtype.dat"),
                ItemFiles.ItemTypeDat(Blade, Potion, Glaive, Helmet));
        }

        public async Task DisposeAsync()
        {
            foreach (var process in this.Processes)
            {
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                catch (InvalidOperationException) { }
                process.Dispose();
            }

            if (Directory.Exists(this.MapDirectory))
                Directory.Delete(this.MapDirectory, recursive: true);

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
                .Concat(new[] { "--Logging:Level=debug", "--Logging:PacketTrace:All=true" }).Concat(args).ToArray());
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

        public async Task<int> RunAccountCommandAsync(params string[] args)
        {
            using var process = this.StartProcess("Comet.Account", "account-cli",
                args.Concat(DatabaseArgs(this.AccountSchema)).ToArray());
            await process.WaitForExitAsync().WaitAsync(StartTimeout);
            return process.ExitCode;
        }

        public Task ExecuteAccountSqlAsync(string sql, params (string, object)[] parameters) =>
            this.ExecuteSqlAsync(this.AccountSchema, sql, parameters);

        public Task ExecuteGameSqlAsync(string sql, params (string, object)[] parameters) =>
            this.ExecuteSqlAsync(this.GameSchema, sql, parameters);

        public async Task<object> QueryGameScalarAsync(string sql, params (string, object)[] parameters)
        {
            await using var db = new MySqlConnection(this.ConnectionString(this.GameSchema));
            await db.OpenAsync();
            var command = new MySqlCommand(sql, db);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return await command.ExecuteScalarAsync();
        }

        private async Task ExecuteSqlAsync(string schema, string sql, (string, object)[] parameters)
        {
            await using var db = new MySqlConnection(this.ConnectionString(schema));
            await db.OpenAsync();
            var command = new MySqlCommand(sql, db);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public string NewEmail(string prefix = "player") =>
            $"{prefix}{Interlocked.Increment(ref this.Counter)}@test{this.RunId}.example";

        public string NewCharacterName(string prefix = "Hero") =>
            $"{prefix}{Interlocked.Increment(ref this.Counter)}";

        public async Task<string> CreateAccountAsync(string password = "secret1")
        {
            var email = this.NewEmail();
            var (account, error) = await AccountsRepository.CreateAsync(email, password);
            Assert.True(account != null, $"Account not created: {error}");
            return email;
        }

        public Task<AccountClient> ConnectAccountAsync() =>
            AccountClient.ConnectAsync(Host, this.AccountPort);

        public Task<GameClient> ConnectGameAsync() =>
            GameClient.ConnectAsync(Host, this.GamePort);

        public async Task<LoginResult> LoginAsync(string email, string password, string realm = Realm)
        {
            await using var account = await this.ConnectAccountAsync();
            return await account.LoginAsync(email, password, realm);
        }

        public Task<Player> PlayAsync(string email, string password, NewCharacter character = null) =>
            Player.LoginAsync(Host, this.AccountPort, Realm, email, password, character);

        public List<JsonElement> Events(string server)
        {
            var dir = Path.Combine(this.LogDirectory, server);
            if (!Directory.Exists(dir))
                return new List<JsonElement>();

            // Files only grow, so each read parses just the lines added since the last
            // one (packet traces make the files large)
            lock (this.LogFiles)
            {
                var events = new List<JsonElement>();
                foreach (var file in Directory.GetFiles(dir, "*.jsonl").OrderBy(f => f))
                {
                    if (!this.LogFiles.TryGetValue(file, out var read))
                        this.LogFiles[file] = read = (0, new List<JsonElement>());

                    using var stream = new FileStream(
                        file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    stream.Seek(read.Position, SeekOrigin.Begin);
                    var added = new byte[stream.Length - read.Position];
                    int length = stream.Read(added, 0, added.Length);

                    // Only whole lines: the server may be halfway through writing one
                    int end = length > 0 ? Array.LastIndexOf(added, (byte)'\n', length - 1) : -1;
                    if (end >= 0)
                    {
                        foreach (var line in System.Text.Encoding.UTF8.GetString(added, 0, end).Split('\n'))
                        {
                            if (line.Length == 0) continue;
                            try { read.Events.Add(JsonDocument.Parse(line).RootElement.Clone()); }
                            catch (JsonException) { }
                        }

                        read.Position += end + 1;
                        this.LogFiles[file] = read;
                    }

                    events.AddRange(read.Events);
                }

                return events;
            }
        }

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

        // Periodic saves log the same event, so only a logout save says the player is gone
        public Task<JsonElement> WaitForLogoutSaveAsync(string name) =>
            this.WaitForEventAsync("game", "character.saved", e =>
                e.GetProperty("name").GetString() == name && e.GetProperty("reason").GetString() == "logout");

        public Task<JsonElement> WaitForEventAsync(string server, string eventName, string field, string value) =>
            this.WaitForEventAsync(server, eventName,
                e => e.TryGetProperty(field, out var v) && v.ToString() == value);
    }

    [CollectionDefinition(Name)]
    public sealed class ServerCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "Servers";
    }
}

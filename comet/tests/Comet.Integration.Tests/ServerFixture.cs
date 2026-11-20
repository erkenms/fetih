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
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Bow = (500301, "LuckyBow", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Arrows = (1050000, "LuckyArrow", 3, 0);

        // Test monsters on 1002, away from the cells other tests use. Pheasants (type 1) attack
        // only players next to them; guards (900) kill monsters within 15 cells and players who
        // hit them. Each spawn has one monster that comes back after RespawnSeconds.
        public const int RespawnSeconds = 2;
        public static readonly (ushort X, ushort Y) PheasantToKill = (820, 820);
        public static readonly (ushort X, ushort Y) PheasantToShoot = (840, 820);
        public static readonly (ushort X, ushort Y) PheasantThatBites = (820, 860);
        public static readonly (ushort X, ushort Y) GuardToProvoke = (860, 860);
        public static readonly (ushort X, ushort Y) GuardOnDuty = (900, 900);
        public static readonly (ushort X, ushort Y) PheasantNearGuard = (903, 900);
        public static readonly (ushort X, ushort Y) PheasantForMagic = (880, 820);
        public static readonly (ushort X, ushort Y) PheasantForBlade = (900, 820);
        public static readonly (ushort X, ushort Y) PheasantForFastBlade = (920, 820);

        // Spells in the test magictype.dat. Thunder kills a pheasant and its first kill
        // reaches level 1; proficiency levels need 0, 100 and 1000 experience.
        public static readonly MagicFiles.Spell Thunder =
            new MagicFiles.Spell(1000, 0, 1, "Thunder") { Power = 1000, Distance = 9, NeedExperience = 20 };
        public static readonly MagicFiles.Spell Thunder1 =
            new MagicFiles.Spell(1000, 1, 1, "Thunder") { Power = 1000, Distance = 9 };
        public static readonly MagicFiles.Spell Superman =
            new MagicFiles.Spell(1025, 0, 6, "Superman") { Power = 31000, StepSeconds = 20, XpSkill = true };
        public static readonly MagicFiles.Spell FastBlade =
            new MagicFiles.Spell(1045, 0, 14, "FastBlade") { Power = 30110, Range = 4, Distance = 10, Stamina = 20, WeaponSubtype = 410 };

        // Short, so a test can revive a dead player
        public const int ReviveSeconds = 1;

        // The market, with the booth flags of Data/npcs.json
        public const uint Market = 1036;

        // Players may fight in Desert City; on a PK field it's no crime (Data/maps.json).
        // Twin City and the market forbid it. Not 1005, the arena: NpcTests need it missing.
        public const uint DesertCity = 1000, PkField = 1080;

        // Shopkeepers in Twin City, with what the test Shop.dat has them sell
        public const uint Blacksmith = 5, Pharmacist = 3;

        // Short, so a test can see a periodic save happen
        public const int SaveIntervalSeconds = 2;

        // Short, so a test can see a dropped item disappear
        public const int GroundExpireSeconds = 4;

        // Short, so a test can see flashing end and PK points go down
        public const int PkFlashSeconds = 2, PkDecaySeconds = 3;

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
            this.WriteSpawns();

            // The game server first, so the account server finds its RPC port
            this.StartServer("Comet.Game", this.GameSchema, "game",
                "--Maps:ClientDirectory=" + this.MapDirectory,
                "--GameNetwork:IPAddress=" + Host, "--GameNetwork:Port=" + this.GamePort,
                "--GameNetwork:MaxConnPerIP=1000", "--GameNetwork:MaxAttemptsPerMinute=100000",
                "--GameNetwork:LoginTimeoutSeconds=" + LoginTimeoutSeconds,
                "--Saving:IntervalSeconds=" + SaveIntervalSeconds,
                "--Ground:ExpireSeconds=" + GroundExpireSeconds,
                "--Monsters:SpawnsFile=" + Path.Combine(this.MapDirectory, "spawns.json"),
                "--Death:ReviveSeconds=" + ReviveSeconds,
                "--Monsters:WanderChance=0",
                "--Pk:FlashSeconds=" + PkFlashSeconds, "--Pk:DecaySeconds=" + PkDecaySeconds,
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
                MapFiles.GameMapDat((1002, "map/map/newplain.7z"), (1006, "map/map/horse.7z"),
                    (Market, "map/map/market.7z"),
                    (DesertCity, "map/map/desert.7z"), (PkField, "map/map/pkfield.7z"),
                    (6000, "map/map/jail.7z")));
            File.WriteAllBytes(Path.Combine(maps, "newplain.DMap"), MapFiles.DMap(972, 972,
                walkable: (x, y) => (x, y) != BlockedCell,
                portals: new[] { StablePortal, UnlinkedPortal }));
            File.WriteAllBytes(Path.Combine(maps, "horse.DMap"), MapFiles.DMap(52, 52));
            File.WriteAllBytes(Path.Combine(maps, "market.DMap"), MapFiles.DMap(400, 400));
            File.WriteAllBytes(Path.Combine(maps, "desert.DMap"), MapFiles.DMap(300, 300));
            File.WriteAllBytes(Path.Combine(maps, "pkfield.DMap"), MapFiles.DMap(100, 100));
            File.WriteAllBytes(Path.Combine(maps, "jail.DMap"), MapFiles.DMap(100, 100));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "itemtype.dat"),
                ItemFiles.ItemTypeDat(Blade, Potion, Glaive, Helmet, Bow, Arrows));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "magictype.dat"),
                MagicFiles.MagicTypeDat(Thunder, Thunder1, Superman, FastBlade));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "WeaponSkillLevelExp.ini"),
                MagicFiles.WeaponSkillLevelExpIni(0, 100, 1000));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "Shop.dat"), ItemFiles.ShopDat(
                (Blacksmith, new[] { Blade.Id, Glaive.Id }), (Pharmacist, new[] { Potion.Id })));
        }

        private void WriteSpawns()
        {
            var spawns = new[]
            {
                (PheasantToKill, 1u), (PheasantToShoot, 1u), (PheasantThatBites, 1u),
                (GuardToProvoke, 900u), (GuardOnDuty, 900u), (PheasantNearGuard, 1u),
                (PheasantForMagic, 1u), (PheasantForBlade, 1u), (PheasantForFastBlade, 1u),
            }.Select((s, i) => $"{{ \"id\": {i + 1}, \"map\": 1002, \"x\": {s.Item1.X}, \"y\": {s.Item1.Y}, " +
                $"\"spread\": 0, \"amount\": 1, \"respawn\": {RespawnSeconds}, \"monster\": {s.Item2} }}");
            File.WriteAllText(Path.Combine(this.MapDirectory, "spawns.json"),
                "{ \"spawns\": [\n" + string.Join(",\n", spawns) + "\n] }\n");
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

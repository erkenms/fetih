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
        public const string StaffRealm = "Staff";
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
        // Their HP and MP, as in the 5619 client's itemtype.dat
        public static readonly (uint Id, string Name, ushort Life, ushort Mana) Potion = (1000000, "Stancher", 70, 0);
        public static readonly (uint Id, string Name, ushort Life, ushort Mana) ManaPotion = (1001000, "Agrypnotic", 0, 70);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Beads = (610003, "WillowPrayerBeads", 1698, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Glaive = (510301, "Glaive", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Helmet = (111003, "IronHelmet", 3899, 15);
        // The helmet a level up, then refined: both sure upgrades
        public static readonly (uint Id, string Name, ushort Amount, byte Level) HelmetLevelUp = (111013, "IronHelmet", 3899, 20);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) HelmetRefined = (111016, "IronHelmet", 3899, 20);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) DragonBall = (1088000, "DragonBall", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Meteor = (1088001, "Meteor", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Emerald = (1080001, "Emerald", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) ExpBall = (723700, "ExpBall", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) MeteorScroll = (720027, "MeteorScroll", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) ThunderBook = (725000, "Thunder", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) SuperDragonGem = (700013, "SuperDragonGem", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) NormalTortoiseGem = (700071, "NormalTortoiseGem", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) SuperThunderGem = (700103, "SuperThunderGem", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Bow = (500301, "LuckyBow", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Katana = (601301, "Katana", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Rapier = (611301, "Rapier", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Pistol = (612301, "Pistol", 1099, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Arrows = (1050020, "DeftArrow", 3, 0);
        // Teleports to Twin City's portal, 430,380 in Data/maps.json
        public static readonly (uint Id, string Name, ushort Amount, byte Level) TwinCityGate = (1060020, "TwinCityGate", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Steed = (300000, "Steed", 100, 0);
        // Its vigor is its dexterity, as the 5619 client's normal RidingCrop
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Crop = (203003, "RidingCrop", 1, 0);
        public const ushort CropVigor = 100;
        // Three and seven days of heaven blessing
        public static readonly (uint Id, string Name, ushort Amount, byte Level) SmallPrayingStone = (1200000, "PrayingStone(S)", 1, 0);
        public static readonly (uint Id, string Name, ushort Amount, byte Level) PrayingStone = (1200001, "PrayingStone(M)", 1, 0);
        // The vigor of a +1 steed, from the test itemadd.ini
        public const ushort SteedVigor = 50;
        // A necklace's health is all from its plus: a +1 HeartNecklace's, by its key in the test itemadd.ini
        public static readonly (uint Id, string Name, ushort Amount, byte Level) Necklace = (120023, "HeartNecklace", 1, 0);
        public const ushort NecklaceHealth = 23;
        // MP from its itemtype, as in the 5619 client's, and no required level
        public static readonly (uint Id, string Name, ushort Life, ushort Mana) Bottle = (2100045, "MagicalBottle", 0, 400);

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
        public static readonly (ushort X, ushort Y) PheasantForTeam = (940, 820);
        public static readonly (ushort X, ushort Y) PheasantForWear = (960, 820);
        public static readonly (ushort X, ushort Y) PheasantForSavedWear = (960, 860);
        public static readonly (ushort X, ushort Y) PheasantForPassive = (900, 860);
        public static readonly (ushort X, ushort Y) PheasantForScatter = (940, 860);
        public static readonly (ushort X, ushort Y) PheasantUnderFlyer = (860, 900);
        public static readonly (ushort X, ushort Y) PheasantForCruelShade = (860, 940);
        public static readonly (ushort X, ushort Y) PheasantInFog = (900, 940);
        public static readonly (ushort X, ushort Y) PheasantForPalm = (860, 700);
        public static readonly (ushort X, ushort Y) PheasantForKick = (900, 700);
        public static readonly (ushort X, ushort Y) PheasantForTripleAttack = (940, 700);
        public static readonly (ushort X, ushort Y) PheasantForOblivion = (860, 740);
        public static readonly (ushort X, ushort Y) PheasantForAdrenaline = (940, 940);
        public static readonly (ushort X, ushort Y) PheasantForWindstorm = (960, 900);
        public static readonly (ushort X, ushort Y) PheasantForFatalStrike = (940, 780);

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
        public static readonly MagicFiles.Spell Cyclone =
            new MagicFiles.Spell(1110, 0, 6, "Cyclone") { Power = 30020, StepSeconds = 20, XpSkill = true, NeedLevel = 3 };
        public static readonly MagicFiles.Spell Hercules =
            new MagicFiles.Spell(1115, 0, 5, "Hercules") { Power = 30085, Range = 3, Distance = 10, Stamina = 30, NeedLevel = 40 };
        public static readonly MagicFiles.Spell Revive =
            new MagicFiles.Spell(1050, 0, 7, "Revive") { Distance = 12, XpSkill = true };
        public static readonly MagicFiles.Spell Meditation =
            new MagicFiles.Spell(1195, 0, 20, "Meditation") { Power = 310, Distance = 5, Stamina = 100, NeedLevel = 44 };

        // A weapon skill that always goes off, for the test blade
        public static readonly MagicFiles.Spell Phoenix =
            new MagicFiles.Spell(5030, 0, 16, "Phoenix") { Power = 30125, Distance = 9, WeaponSubtype = 410, AutoActive = 4, NeedLevel = 15 };
        public static readonly MagicFiles.Spell Restore =
            new MagicFiles.Spell(1105, 0, 2, "Restore") { Power = -32768, Distance = 12, XpSkill = true };
        public static readonly MagicFiles.Spell Fly =
            new MagicFiles.Spell(8002, 0, 6, "Fly") { Power = 1, StepSeconds = 40, XpSkill = true, WeaponSubtype = 500 };
        public static readonly MagicFiles.Spell CruelShade =
            new MagicFiles.Spell(3050, 0, 26, "CruelShade") { Power = 30010, Distance = 5, Stamina = 100 };
        // Always poisons, every second, three times
        public static readonly MagicFiles.Spell ToxicFog =
            new MagicFiles.Spell(6001, 0, 27, "ToxicFog") { Power = 30010, Range = 4, Distance = 10, StepSeconds = 1, ActiveTimes = 3 };
        public static readonly MagicFiles.Spell Scatter =
            new MagicFiles.Spell(8001, 0, 4, "ScatterFire") { Power = 30050, Range = 8, Distance = 15, WeaponSubtype = 500, UseItem = 50, UseItemCount = 2 };
        public static readonly MagicFiles.Spell RadiantPalm =
            new MagicFiles.Spell(10381, 0, 48, "RadiantPalm") { Power = 30100, Range = 5, Distance = 4, Stamina = 20, WeaponSubtype = 610 };
        public static readonly MagicFiles.Spell WhirlwindKick =
            new MagicFiles.Spell(10415, 0, 54, "WhirlwindKick") { Power = 30065, Range = 3, Stamina = 20, WeaponSubtype = 610 };
        // Always goes off, for the test beads
        public static readonly MagicFiles.Spell TripleAttack =
            new MagicFiles.Spell(10490, 0, 55, "TripleAttack") { Percent = 100, Range = 30, StepSeconds = 1000000, WeaponSubtype = 610 };
        public static readonly MagicFiles.Spell TyrantAura =
            new MagicFiles.Spell(10395, 0, 51, "TyrantAura") { Power = 2, Range = 30, StepSeconds = 1000000 };
        public static readonly MagicFiles.Spell FendAura =
            new MagicFiles.Spell(10410, 0, 51, "FendAura") { Power = 2, Range = 30, StepSeconds = 1000000 };
        public static readonly MagicFiles.Spell SoulShackle =
            new MagicFiles.Spell(10405, 0, 6, "SoulShackle") { Distance = 15, StepSeconds = 30 };
        public static readonly MagicFiles.Spell Serenity =
            new MagicFiles.Spell(10400, 0, 46, "Serenity") { StepSeconds = 10 };
        public static readonly MagicFiles.Spell Tranquility =
            new MagicFiles.Spell(10425, 0, 47, "Tranquility") { Distance = 20, StepSeconds = 2 };
        public static readonly MagicFiles.Spell Compassion =
            new MagicFiles.Spell(10430, 0, 50, "Compassion") { Range = 20, Distance = 20 };
        public static readonly MagicFiles.Spell Oblivion =
            new MagicFiles.Spell(10390, 0, 53, "Oblivion") { Power = 33, StepSeconds = 30, XpSkill = true };
        public static readonly MagicFiles.Spell ScurvyBomb =
            new MagicFiles.Spell(11040, 0, 56, "ScurvyBomb") { Power = 30010, Range = 4, Distance = 10, StepSeconds = 10, Stamina = 20 };
        public static readonly MagicFiles.Spell GaleBomb =
            new MagicFiles.Spell(11070, 0, 60, "GaleBomb") { Power = 30020, Range = 5, Distance = 10, Stamina = 20 };
        public static readonly MagicFiles.Spell BladeTempest =
            new MagicFiles.Spell(11110, 0, 61, "BladeTempest") { Power = 30095, Range = 1, Distance = 20, Stamina = 20, WeaponSubtype = 611 };
        public static readonly MagicFiles.Spell EagleEye =
            new MagicFiles.Spell(11030, 0, 1, "EagleEye") { Power = 30028, Distance = 15, WeaponSubtype = 612 };
        public static readonly MagicFiles.Spell KrakensRevenge =
            new MagicFiles.Spell(11100, 0, 62, "Kraken`sRevenge") { Range = 5, Distance = 15, StepSeconds = 8, Stamina = 35, WeaponSubtype = 612 };
        public static readonly MagicFiles.Spell BlackSpot =
            new MagicFiles.Spell(11120, 0, 58, "BlackSpot") { Percent = 100, StepSeconds = 8, WeaponSubtype = 612 };
        public static readonly MagicFiles.Spell AdrenalineRush =
            new MagicFiles.Spell(11130, 0, 59, "AdrenalineRush") { Percent = 100, AutoActive = 8 };
        public static readonly MagicFiles.Spell CannonBarrage =
            new MagicFiles.Spell(11050, 0, 57, "CannonBarrage") { Power = 30500, Range = 5, Distance = 10, StepSeconds = 60, XpSkill = true, WeaponSubtype = 611 };
        public static readonly MagicFiles.Spell BlackbeardsRage =
            new MagicFiles.Spell(11060, 0, 63, "Blackbeard`sRage") { Power = 30025, Range = 5, Distance = 12, StepSeconds = 60, XpSkill = true, WeaponSubtype = 612, ActiveTimes = 3 };
        public static readonly MagicFiles.Spell Windstorm =
            new MagicFiles.Spell(11140, 0, 55, "Windstorm") { Percent = 100, Range = 30, StepSeconds = 1000000, WeaponSubtype = 611 };
        public static readonly MagicFiles.Spell Dodge =
            new MagicFiles.Spell(3080, 0, 6, "Dodge") { Power = 30110, StepSeconds = 60, Distance = 10, Target = 1, Status = 21 };
        public static readonly MagicFiles.Spell IronShirt =
            new MagicFiles.Spell(5100, 0, 6, "IronShirt") { Power = 30400, StepSeconds = 20, XpSkill = true, Target = 2, Status = 116 };
        public static readonly MagicFiles.Spell Intensify =
            new MagicFiles.Spell(9000, 0, 6, "Intensify") { Power = 30200, StepSeconds = 60, Distance = 15, Target = 2, Status = 23 };
        public static readonly MagicFiles.Spell Bless =
            new MagicFiles.Spell(9876, 0, 6, "Bless") { Power = 1, StepSeconds = 1000000, Stamina = 100, Target = 2, Status = 25 };
        public static readonly MagicFiles.Spell AzureShield =
            new MagicFiles.Spell(30000, 0, 6, "AzureShield") { Stamina = 30, Distance = 20, Target = 1, Status = 89 };
        public static readonly MagicFiles.Spell ShurikenVortex =
            new MagicFiles.Spell(6010, 0, 28, "ShurikenVortex") { Power = 30030, Range = 12, Distance = 15, StepSeconds = 2, ActiveTimes = 10, XpSkill = true, Target = 2, Status = 39 };
        public static readonly MagicFiles.Spell FatalStrike =
            new MagicFiles.Spell(6011, 0, 6, "FatalStrike") { Power = 30500, Distance = 18, StepSeconds = 60, XpSkill = true, WeaponSubtype = 601511, Target = 2, Status = 40 };
        // Sure, where the client's is 80% at an equal battle power
        public static readonly MagicFiles.Spell PoisonStar =
            new MagicFiles.Spell(6002, 0, 6, "PoisonStar") { Power = 1, Percent = 100, StepSeconds = 5, Range = 5, Distance = 10, Stamina = 50, Status = 42 };
        public static readonly MagicFiles.Spell Riding =
            new MagicFiles.Spell(7001, 0, 32, "Riding") { Power = 1, Stamina = 20, StepSeconds = 1000000 };

        // Short, so a test can revive a dead player
        public const int ReviveSeconds = 1;
        public const int TrainingOnlineSeconds = 2;

        // The class masters' hall of Data/npcs.json
        public const uint JobCenter = 1004;

        // The market, with the booth flags of Data/npcs.json
        public const uint Market = 1036;

        // Players may fight in Desert City; on a PK field it's no crime (Data/maps.json).
        // Twin City and the market forbid it. Not 1005, the arena: NpcTests need it missing.
        public const uint DesertCity = 1000, PkField = 1080;

        // Shopkeepers in Twin City, with what the test Shop.dat has them sell
        public const uint Blacksmith = 5, Pharmacist = 3;

        // The qualifier arena, and its timings: short, but long enough to try an attack
        // during the countdown and to accept a match
        public const uint Arena = 900000;
        public const int ArenaInviteSeconds = 3, ArenaCountdownSeconds = 3, ArenaEndSeconds = 1;

        // Short, so a test can see a periodic save happen
        public const int SaveIntervalSeconds = 2;

        // Short, so a test can see a dropped item disappear
        public const int GroundExpireSeconds = 4;

        // Short, so a test can see flashing end and PK points go down
        public const int PkFlashSeconds = 2, PkDecaySeconds = 3;

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

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
        // Each server's events in the order they were read, and how far each log file was read
        private readonly Dictionary<string, List<JsonElement>> ServerEvents = new Dictionary<string, List<JsonElement>>();
        private readonly Dictionary<string, long> LogFiles = new Dictionary<string, long>();
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

            await this.DropStaleSchemasAsync();
            try { await this.StartAsync(); }
            catch
            {
                await this.DisposeAsync();
                throw;
            }
        }

        // Runs that were killed never dropped their schemas. Only ones hours old go, so a run
        // going on at the same time keeps its own. A schema without tables is from a load that
        // failed, since a load creates the schema and its tables in one batch.
        private async Task DropStaleSchemasAsync()
        {
            await using var db = new MySqlConnection(this.ConnectionString());
            await db.OpenAsync();
            var stale = new List<string>();
            var query = new MySqlCommand(
                "SELECT s.SCHEMA_NAME FROM information_schema.SCHEMATA s " +
                "LEFT JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = s.SCHEMA_NAME " +
                "WHERE s.SCHEMA_NAME LIKE 'comet\\_test\\_%' GROUP BY s.SCHEMA_NAME " +
                "HAVING COUNT(t.TABLE_NAME) = 0 OR MAX(t.CREATE_TIME) < NOW() - INTERVAL 6 HOUR", db);
            await using (var reader = await query.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    stale.Add(reader.GetString(0));
            foreach (var schema in stale)
                await new MySqlCommand($"DROP DATABASE IF EXISTS `{schema}`", db).ExecuteNonQueryAsync();
        }

        private async Task StartAsync()
        {
            await this.LoadSchemaAsync("comet.account.sql", "comet.account", this.AccountSchema);
            await this.LoadSchemaAsync("comet.game.sql", "comet.game", this.GameSchema);
            this.AccountPort = FreePort();
            this.GamePort = FreePort();
            int rpcPort = FreePort();
            await this.ExecuteAccountSqlAsync(
                "UPDATE realm SET GameIPAddress = @host, RpcIPAddress = @host, GamePort = @game, " +
                "RpcPort = @rpc WHERE Name = @realm",
                ("@host", Host), ("@game", this.GamePort), ("@rpc", rpcPort), ("@realm", Realm));
            await this.ExecuteAccountSqlAsync(
                "INSERT INTO realm (Name, AuthorityID, GameIPAddress, RpcIPAddress, GamePort, RpcPort) " +
                "VALUES (@realm, 3, @host, @host, @game, @rpc)",
                ("@host", Host), ("@game", this.GamePort), ("@rpc", rpcPort), ("@realm", StaffRealm));

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
                "--Training:OnlineSeconds=" + TrainingOnlineSeconds,
                "--Monsters:WanderChance=0",
                "--Pk:FlashSeconds=" + PkFlashSeconds, "--Pk:DecaySeconds=" + PkDecaySeconds,
                "--Arena:InviteSeconds=" + ArenaInviteSeconds, "--Arena:CountdownSeconds=" + ArenaCountdownSeconds,
                "--Arena:EndSeconds=" + ArenaEndSeconds, "--Magic:MinimumCastMilliseconds=0",
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
                    (Market, "map/map/market.7z"), (JobCenter, "map/map/jobcenter.7z"),
                    (DesertCity, "map/map/desert.7z"), (PkField, "map/map/pkfield.7z"),
                    (6000, "map/map/jail.7z"), (Arena, "map/map/newarena.7z"),
                    (601, "map/map/offlinetg.7z")));
            File.WriteAllBytes(Path.Combine(maps, "newplain.DMap"), MapFiles.DMap(972, 972,
                walkable: (x, y) => (x, y) != BlockedCell,
                portals: new[] { StablePortal, UnlinkedPortal }));
            File.WriteAllBytes(Path.Combine(maps, "horse.DMap"), MapFiles.DMap(52, 52));
            File.WriteAllBytes(Path.Combine(maps, "market.DMap"), MapFiles.DMap(400, 400));
            File.WriteAllBytes(Path.Combine(maps, "jobcenter.DMap"), MapFiles.DMap(96, 96));
            File.WriteAllBytes(Path.Combine(maps, "desert.DMap"), MapFiles.DMap(300, 300));
            File.WriteAllBytes(Path.Combine(maps, "pkfield.DMap"), MapFiles.DMap(100, 100));
            File.WriteAllBytes(Path.Combine(maps, "jail.DMap"), MapFiles.DMap(100, 100));
            File.WriteAllBytes(Path.Combine(maps, "offlinetg.DMap"), MapFiles.DMap(100, 100));
            File.WriteAllBytes(Path.Combine(maps, "newarena.DMap"), MapFiles.DMap(100, 100));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "itemtype.dat"),
                ItemFiles.ItemTypeDat(new[] { Blade, Beads, Glaive, Helmet, Bow, Katana, Rapier, Pistol, Arrows, TwinCityGate, Steed, Crop,
                    SmallPrayingStone, PrayingStone, Necklace,
                    HelmetLevelUp, HelmetRefined, DragonBall, Meteor, Emerald,
                    SuperDragonGem, NormalTortoiseGem, SuperThunderGem, ExpBall, MeteorScroll, ThunderBook }, new[] { Potion, ManaPotion, Bottle },
                    new Dictionary<uint, ushort> { [Crop.Id] = CropVigor }));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "itemadd.ini"),
                ItemFiles.ItemAddIni((Steed.Id, 1, 0, SteedVigor), (120020, 1, NecklaceHealth, 0)));
            File.WriteAllBytes(Path.Combine(this.MapDirectory, "ini", "magictype.dat"),
                MagicFiles.MagicTypeDat(Thunder, Thunder1, Superman, FastBlade, Riding,
                    Cyclone, Hercules, Revive, Meditation, Phoenix, Scatter, Restore, Fly, CruelShade, ToxicFog,
                    RadiantPalm, WhirlwindKick, TripleAttack, TyrantAura, FendAura,
                    SoulShackle, Serenity, Tranquility, Compassion, Oblivion,

                    ScurvyBomb, GaleBomb, BladeTempest, EagleEye, KrakensRevenge, BlackSpot, AdrenalineRush,
                    CannonBarrage, BlackbeardsRage, Windstorm,
                    Dodge, IronShirt, Intensify, Bless, AzureShield, ShurikenVortex, FatalStrike, PoisonStar));
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
                (PheasantForTeam, 1u), (PheasantForWear, 1u), (PheasantForSavedWear, 1u), (PheasantForPassive, 1u), (PheasantForScatter, 1u), (PheasantUnderFlyer, 1u), (PheasantForCruelShade, 1u), (PheasantInFog, 1u),
                (PheasantForPalm, 1u), (PheasantForKick, 1u), (PheasantForTripleAttack, 1u), (PheasantForOblivion, 1u),
                (PheasantForAdrenaline, 1u), (PheasantForWindstorm, 1u), (PheasantForFatalStrike, 1u),
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

        public async Task<MySqlConnection> OpenGameDatabaseAsync()
        {
            var db = new MySqlConnection(this.ConnectionString(this.GameSchema));
            await db.OpenAsync();
            return db;
        }

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

        public IReadOnlyList<JsonElement> Events(string server)
        {
            lock (this.LogFiles)
                return this.Read(server).ToList();
        }

        // How many events the server has logged so far: a wait given it as `after` only
        // accepts events logged since
        public int Mark(string server = "game")
        {
            lock (this.LogFiles)
                return this.Read(server).Count;
        }

        // Files only grow, so each read parses just the lines added since the last one
        // (packet traces make the files large). Callers hold the lock.
        private List<JsonElement> Read(string server)
        {
            if (!this.ServerEvents.TryGetValue(server, out var events))
                this.ServerEvents[server] = events = new List<JsonElement>();
            var dir = Path.Combine(this.LogDirectory, server);
            if (!Directory.Exists(dir))
                return events;

            foreach (var file in Directory.GetFiles(dir, "*.jsonl").OrderBy(f => f))
            {
                this.LogFiles.TryGetValue(file, out long position);
                using var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length <= position)
                    continue;
                stream.Seek(position, SeekOrigin.Begin);
                var added = new byte[stream.Length - position];
                int length = stream.Read(added, 0, added.Length);

                // Only whole lines: the server may be halfway through writing one
                int end = length > 0 ? Array.LastIndexOf(added, (byte)'\n', length - 1) : -1;
                if (end < 0)
                    continue;
                foreach (var line in System.Text.Encoding.UTF8.GetString(added, 0, end).Split('\n'))
                {
                    if (line.Length == 0) continue;
                    try { events.Add(JsonDocument.Parse(line).RootElement.Clone()); }
                    catch (JsonException) { }
                }
                this.LogFiles[file] = position + end + 1;
            }
            return events;
        }

        public async Task<JsonElement> WaitForEventAsync(
            string server, string eventName, Func<JsonElement, bool> match, TimeSpan? timeout = null, int after = 0)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            int next = after;
            while (true)
            {
                JsonElement[] added;
                lock (this.LogFiles)
                {
                    var events = this.Read(server);
                    added = events.Skip(next).ToArray();
                    next = Math.Max(next, events.Count);
                }
                foreach (var e in added)
                    if (e.TryGetProperty("event", out var name) && name.GetString() == eventName && match(e))
                        return e;
                if (DateTime.UtcNow > deadline)
                    throw new Xunit.Sdk.XunitException(
                        $"The {server} server didn't log {eventName} in time (logs in {this.LogDirectory})");
                await Task.Delay(20);
            }
        }

        public Task<JsonElement> WaitForEventAsync(
            string server, string eventName, string field, string value, int after = 0) =>
            this.WaitForEventAsync(server, eventName,
                e => e.TryGetProperty(field, out var v) && v.ToString() == value, after: after);

        // Periodic saves log the same event, so only a logout save says the player is gone.
        // Every login of a character ends in one, so only one logged after `after` counts.
        public Task<JsonElement> WaitForLogoutSaveAsync(string name, int after) =>
            this.WaitForEventAsync("game", "character.saved", e =>
                e.GetProperty("name").GetString() == name && e.GetProperty("reason").GetString() == "logout",
                after: after);

        public async Task<uint> AccountIdAsync(string email)
        {
            await using var db = new MySqlConnection(this.ConnectionString(this.AccountSchema));
            await db.OpenAsync();
            var command = new MySqlCommand("SELECT AccountID FROM account WHERE Username = @email", db);
            command.Parameters.AddWithValue("@email", email);
            return Convert.ToUInt32(await command.ExecuteScalarAsync());
        }
    }

    [CollectionDefinition(Name)]
    public sealed class ServerCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "Servers";
    }
}

namespace Fetih.Game.Database
{
    using Fetih.Core.Logging;
    using Microsoft.Extensions.Configuration;

    public class ServerConfiguration 
    {
        public DatabaseConfiguration Database { get; set; }

        // Where /ban and /unban set account status
        public DatabaseConfiguration AccountDatabase { get; set; }
        public GameNetworkConfiguration GameNetwork { get; set; }
        public RpcNetworkConfiguration RpcNetwork { get; set; }
        public AuthenticationConfiguration Authentication { get; set; }
        public LoggingConfiguration Logging { get; set; }
        public MapsConfiguration Maps { get; set; }
        public DataConfiguration Data { get; set; } = new DataConfiguration();
        public SavingConfiguration Saving { get; set; } = new SavingConfiguration();
        public GroundConfiguration Ground { get; set; } = new GroundConfiguration();
        public MonstersConfiguration Monsters { get; set; } = new MonstersConfiguration();
        public DeathConfiguration Death { get; set; } = new DeathConfiguration();
        public TrainingConfiguration Training { get; set; } = new TrainingConfiguration();
        public CombatConfiguration Combat { get; set; } = new CombatConfiguration();
        public PkConfiguration Pk { get; set; } = new PkConfiguration();
        public MagicConfiguration Magic { get; set; } = new MagicConfiguration();
        public ArenaConfiguration Arena { get; set; } = new ArenaConfiguration();
        public GuildWarConfiguration GuildWar { get; set; } = new GuildWarConfiguration();
        public ClanWarConfiguration ClanWar { get; set; } = new ClanWarConfiguration();
        public ElitePkConfiguration ElitePk { get; set; } = new ElitePkConfiguration();
        public TeamPkConfiguration TeamPk { get; set; } = new TeamPkConfiguration();
        public CaptureTheFlagConfiguration CaptureTheFlag { get; set; } = new CaptureTheFlagConfiguration();
        public GuildPkConfiguration GuildPk { get; set; } = new GuildPkConfiguration();
        public BroadcastConfiguration Broadcast { get; set; } = new BroadcastConfiguration();
        public MetricsConfiguration Metrics { get; set; } = new MetricsConfiguration();

        // The zone whose midnight starts a new day, as an IANA or Windows ID; the machine's
        // own zone when empty
        public string TimeZone { get; set; }

        public class DatabaseConfiguration
        {
            public string Hostname { get; set; }
            public string Schema { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class GameNetworkConfiguration
        {
            public string IPAddress { get; set; }
            public int Port { get; set; }
            public int MaxConn { get; set; }

            // Connections from one IP address: more at once, or more attempts in a
            // minute, blocks the address for 15 minutes
            public int MaxConnPerIP { get; set; } = 10;
            public int MaxAttemptsPerMinute { get; set; } = 15;

            // Time a new connection has to log in (MsgConnect). There's no idle limit
            // after that; dead connections are found by TCP keepalive.
            public int LoginTimeoutSeconds { get; set; } = 60;
        }

        public class RpcNetworkConfiguration
        {
            public string IPAddress { get; set; }
            public int Port { get; set; }
        }

        public class AuthenticationConfiguration
        {
            public bool StrictAuthPass { get; set; }
        }

        // Relative to the server's own folder, so it's found however the server is started.
        public class MapsConfiguration
        {
            public string ClientDirectory { get; set; }

            public string ResolveClientDirectory() =>
                System.IO.Path.GetFullPath(this.ClientDirectory, System.AppContext.BaseDirectory);
        }

        // Where the Data/*.json files are read from, relative to the server's own folder; its
        // own Data/ copy when empty
        public class DataConfiguration
        {
            public string Directory { get; set; }

            public string ResolveDirectory() => string.IsNullOrEmpty(this.Directory)
                ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data")
                : System.IO.Path.GetFullPath(this.Directory, System.AppContext.BaseDirectory);
        }

        // Online characters are saved this often, besides on logout
        public class SavingConfiguration
        {
            public int IntervalSeconds { get; set; } = 300;

            // How long a shutdown waits for the logout saves; podman stop must wait longer
            public int ShutdownSeconds { get; set; } = 20;
        }

        public class GroundConfiguration
        {
            public int ExpireSeconds { get; set; } = 60;
        }

        public class MonstersConfiguration
        {
            // A file in Data/ or a full path
            public string SpawnsFile { get; set; } = "spawns.json";
            public string BossesFile { get; set; } = "bosses.json";
            public string DropsFile { get; set; } = "drops.json";

            // Chance in percent that an idle monster takes a random step, ten times a second
            public int WanderChance { get; set; } = 10;

            // How long a monster watches a player before going for them, and turns on what hit it
            public int ReactionMinMilliseconds { get; set; } = 400;
            public int ReactionMaxMilliseconds { get; set; } = 900;
            public int GuardReactionMinMilliseconds { get; set; } = 150;
            public int GuardReactionMaxMilliseconds { get; set; } = 350;
            public int ProvokedMinMilliseconds { get; set; } = 150;
            public int ProvokedMaxMilliseconds { get; set; } = 350;

            // How long after landing a jumping player goes unnoticed by monsters
            public int JumpGraceMilliseconds { get; set; } = 1000;

            // How far from home monsters and guards chase, wander and patrol
            public int LeashRange { get; set; } = 20;
            public int GuardLeashRange { get; set; } = 12;
            public int WanderRange { get; set; } = 5;
            public int GuardPatrolRange { get; set; } = 3;
        }

        // How long a dead player waits before the Revive button works
        public class DeathConfiguration
        {
            public int ReviveSeconds { get; set; } = 20;
        }

        // How often a blessed player's online training EXP grows
        public class TrainingConfiguration
        {
            public int OnlineSeconds { get; set; } = 60;
        }

        // The shortest time between two swings. Weapons and monster data give 500-1000 ms, which
        // looks too fast in the client.
        public class CombatConfiguration
        {
            public int PlayerAttackMilliseconds { get; set; } = 1500;
            public int MonsterAttackMilliseconds { get; set; } = 1500;
        }

        // How long attacking an innocent player makes the attacker flash, and how often a
        // player online loses a PK point
        public class PkConfiguration
        {
            public int FlashSeconds { get; set; } = 30;
            public int DecaySeconds { get; set; } = 360;
        }

        // How fast the XP bar fills (a point each XpMilliseconds) and how long it stays full, and
        // the shortest time between casts, whatever a spell's delay says
        public class MagicConfiguration
        {
            public int XpMilliseconds { get; set; } = 3000;
            public int XpFullSeconds { get; set; } = 20;
            public int MinimumCastMilliseconds { get; set; } = 500;
        }

        // The qualifier: time to accept a match, the countdown before fighting, the longest
        // fight, and how long fighters stay in the arena after it
        public class ArenaConfiguration
        {
            public int InviteSeconds { get; set; } = 60;
            public int CountdownSeconds { get; set; } = 10;
            public int MatchSeconds { get; set; } = 300;
            public int EndSeconds { get; set; } = 3;
        }

        // Weekly Guild War window in the server's local time zone, plus pole/gate life and prize
        public class GuildWarConfiguration
        {
            public System.DayOfWeek StartDay { get; set; } = System.DayOfWeek.Saturday;
            public int StartHour { get; set; } = 20;
            public int StartMinute { get; set; } = 0;
            public System.DayOfWeek EndDay { get; set; } = System.DayOfWeek.Sunday;
            public int EndHour { get; set; } = 20;
            public int EndMinute { get; set; } = 0;
            public uint PoleLife { get; set; } = 20_000_000;
            public uint GateLife { get; set; } = 10_000_000;
            public uint PrizeConquerPoints { get; set; } = 3_000;
        }

        // Daily Clan War in the server's local time zone: sign-up closes at SignUpClose and
        // reopens at the war's end; the battlefield takes entrants for EntryMinutes after the start
        // Elite PK Tournament match timings; its days and times come from the client's Activity.ini
        public class ElitePkConfiguration
        {
            public int KnockoutSeconds { get; set; } = 180;
            public int FinalSeconds { get; set; } = 300;
            public int CountdownSeconds { get; set; } = 10;
            public int RoundBreakSeconds { get; set; } = 15;
            public int LeaveSeconds { get; set; } = 3;
        }

        // Team PK and Skill Team PK match timings, as the client's rule texts give them
        public class TeamPkConfiguration
        {
            public int KnockoutSeconds { get; set; } = 600;
            public int FinalSeconds { get; set; } = 900;
            public int CountdownSeconds { get; set; } = 10;
            public int RoundBreakSeconds { get; set; } = 15;
            public int LeaveSeconds { get; set; } = 3;
        }

        // Capture the Flag; its day and hours come from the client's Activity.ini. Seconds to plant
        // a flag are the client's rule tip; the flags out at once and how often a base turns X2
        // aren't known from the client.
        public class CaptureTheFlagConfiguration
        {
            public int FlagSeconds { get; set; } = 60;
            public int Flags { get; set; } = 10;
            public int DoubleSeconds { get; set; } = 300;
        }

        // The Guild PK Tournament's flag life, how long members must have been in their guild to
        // fight, and the Conquer Points a leader or deputy claims for places 1-8 (GuildPk's when
        // unset). The client gives no flag life or prize amounts; these are the server's own.
        public class GuildPkConfiguration
        {
            public uint FlagLife { get; set; } = 1_000_000;
            public int MinGuildDays { get; set; } = 7;
            public uint[] ContestPrizeConquerPoints { get; set; }
            public uint[] TournamentPrizeConquerPoints { get; set; }
        }

        public class ClanWarConfiguration
        {
            public int StartHour { get; set; } = 20;
            public int StartMinute { get; set; } = 30;
            public int EndHour { get; set; } = 21;
            public int EndMinute { get; set; } = 0;
            public int SignUpCloseHour { get; set; } = 20;
            public int SignUpCloseMinute { get; set; } = 0;
            public int EntryMinutes { get; set; } = 5;
            public ulong Fee { get; set; } = 500_000;
        }

        // How often the next queued broadcast message is released to the server
        public class BroadcastConfiguration
        {
            public int IntervalSeconds { get; set; } = 60;
        }

        public ServerConfiguration(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("Fetih.Game.config")
                .AddEnvironmentVariables("FETIH_GAME_")
                .AddCommandLine(args)
                .Build()
                .Bind(this);

            // Without its own section, the account database is on the game database's server
            if (this.AccountDatabase == null && this.Database != null)
                this.AccountDatabase = new DatabaseConfiguration
                {
                    Hostname = this.Database.Hostname, Schema = "fetih_account",
                    Username = this.Database.Username, Password = this.Database.Password
                };
        }

        public bool Valid => 
            this.Database != null && 
            this.GameNetwork != null &&
            this.RpcNetwork != null &&
            this.Authentication != null &&
            !string.IsNullOrEmpty(this.Maps?.ClientDirectory);
    }
}
namespace Comet.Game.Database
{
    using Comet.Core.Logging;
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
        public SavingConfiguration Saving { get; set; } = new SavingConfiguration();
        public GroundConfiguration Ground { get; set; } = new GroundConfiguration();
        public MonstersConfiguration Monsters { get; set; } = new MonstersConfiguration();
        public DeathConfiguration Death { get; set; } = new DeathConfiguration();
        public TrainingConfiguration Training { get; set; } = new TrainingConfiguration();
        public CombatConfiguration Combat { get; set; } = new CombatConfiguration();
        public PkConfiguration Pk { get; set; } = new PkConfiguration();
        public MagicConfiguration Magic { get; set; } = new MagicConfiguration();
        public ArenaConfiguration Arena { get; set; } = new ArenaConfiguration();

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

        // Online characters are saved this often, besides on logout
        public class SavingConfiguration
        {
            public int IntervalSeconds { get; set; } = 300;
        }

        public class GroundConfiguration
        {
            public int ExpireSeconds { get; set; } = 60;
        }

        public class MonstersConfiguration
        {
            // A file in Data/ or a full path
            public string SpawnsFile { get; set; } = "spawns.json";

            // Chance in percent that an idle monster takes a random step, ten times a second
            public int WanderChance { get; set; } = 10;
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

        public ServerConfiguration(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("Comet.Game.config")
                .AddCommandLine(args)
                .Build()
                .Bind(this);

            // Without its own section, the account database is on the game database's server
            if (this.AccountDatabase == null && this.Database != null)
                this.AccountDatabase = new DatabaseConfiguration
                {
                    Hostname = this.Database.Hostname, Schema = "comet.account",
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
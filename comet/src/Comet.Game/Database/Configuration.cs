namespace Comet.Game.Database
{
    using Comet.Core.Logging;
    using Microsoft.Extensions.Configuration;

    public class ServerConfiguration 
    {
        public DatabaseConfiguration Database { get; set; }
        public GameNetworkConfiguration GameNetwork { get; set; }
        public RpcNetworkConfiguration RpcNetwork { get; set; }
        public AuthenticationConfiguration Authentication { get; set; }
        public LoggingConfiguration Logging { get; set; }
        public MapsConfiguration Maps { get; set; }
        public SavingConfiguration Saving { get; set; } = new SavingConfiguration();

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

        public ServerConfiguration(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("Comet.Game.config")
                .AddCommandLine(args)
                .Build()
                .Bind(this);
        }

        public bool Valid => 
            this.Database != null && 
            this.GameNetwork != null &&
            this.RpcNetwork != null &&
            this.Authentication != null &&
            !string.IsNullOrEmpty(this.Maps?.ClientDirectory);
    }
}
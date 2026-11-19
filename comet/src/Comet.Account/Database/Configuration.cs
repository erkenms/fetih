namespace Comet.Account.Database
{
    using Comet.Core.Logging;
    using Microsoft.Extensions.Configuration;

    public class ServerConfiguration 
    {
        public DatabaseConfiguration Database { get; set; }
        public NetworkConfiguration Network { get; set; }
        public LoggingConfiguration Logging { get; set; }

        public class DatabaseConfiguration
        {
            public string Hostname { get; set; }
            public string Schema { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class NetworkConfiguration
        {
            public string IPAddress { get; set; }
            public int Port { get; set; }
            public int MaxConn { get; set; }

            // Connections from one IP address: more at once, or more attempts in a
            // minute, blocks the address for 15 minutes
            public int MaxConnPerIP { get; set; } = 10;
            public int MaxAttemptsPerMinute { get; set; } = 15;

            // Time a connection may stay open before logging in. The account server
            // never marks a connection logged in (it redirects the client within
            // seconds), so this caps its connections.
            public int LoginTimeoutSeconds { get; set; } = 60;
        }

        public ServerConfiguration(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("Comet.Account.config")
                .AddCommandLine(args)
                .Build()
                .Bind(this);
        }

        public bool Valid => 
            this.Database != null && 
            this.Network != null;
    }
}

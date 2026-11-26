namespace Comet.Bots
{
    using System.Collections.Generic;

    public sealed class BotsConfiguration
    {
        public ServerAddress AccountServer { get; set; } = new ServerAddress();
        public Database AccountDatabase { get; set; } = new Database();
        public Database GameDatabase { get; set; } = new Database();
        public string ClientDirectory { get; set; }
        public string Password { get; set; } = "botpass1";
        public string Prefix { get; set; } = "Bot";
        public int LoginSpreadSeconds { get; set; } = 30;
        public int ReportSeconds { get; set; } = 10;
        public List<Group> Groups { get; set; } = new List<Group>();

        public sealed class ServerAddress
        {
            public string Host { get; set; } = "127.0.0.1";
            public int Port { get; set; } = 9960;
            public string Realm { get; set; } = "Altay";

            // The game server's address in place of the realm's; empty to use the realm's
            public string GameHost { get; set; }
        }

        public sealed class Database
        {
            public string Hostname { get; set; } = "localhost";
            public string Schema { get; set; }
            public string Username { get; set; } = "root";
            public string Password { get; set; }
        }

        public sealed class Group
        {
            public string Behavior { get; set; } = "idle";
            public int Count { get; set; }
            public uint Map { get; set; } = 1002;
            public ushort X { get; set; }
            public ushort Y { get; set; }
            public int Spread { get; set; } = 10;
            public byte Level { get; set; } = 1;
        }
    }
}

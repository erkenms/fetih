namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using Comet.Network.RPC;
    using Comet.Network.Security;
    using Serilog;

    /// <summary>
    /// The game server listens for authentication players with a valid access token from
    /// the account server, and hosts the game world. The game world in this project has 
    /// been simplified into a single server executable. For an n-server distributed 
    /// systems implementation of a Conquer Online server, see Chimera. 
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // Copyright notice may not be commented out. If adding your own copyright or
            // claim of ownership, you may include a second copyright above the existing
            // copyright. Do not remove completely to comply with software license. The
            // project name and version may be removed or changed.
            Console.Title = "Comet, Game Server";
            Console.WriteLine();
            Console.WriteLine("  Comet: Game Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine("  Patch 5619");
            Console.WriteLine();

            // Read configuration file and command-line arguments
            var config = new ServerConfiguration(args);
            LogSetup.Configure("game", config.Logging);
            if (!config.Valid) 
            {
                Log.Error("Invalid server configuration file");
                Log.CloseAndFlush();
                return;
            }

            // Initialize the database
            Log.Information("Initializing server...");
            MsgConnect.StrictAuthentication = config.Authentication.StrictAuthPass;
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Log.Error("Invalid database configuration");
                Log.CloseAndFlush();
                return;
            }

            // Read the map list (grids are loaded when a map is first used)
            try
            {
                Kernel.Maps = new MapStore(config.Maps.ClientDirectory);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read map data from {Directory}; set Maps:ClientDirectory " +
                    "to a copy of the 5619 client", config.Maps.ClientDirectory);
                Log.CloseAndFlush();
                return;
            }

            // Start background services
            var tasks = new List<Task>();
            tasks.Add(Kernel.Services.Randomness.StartAsync(CancellationToken.None));
            tasks.Add(DiffieHellman.ProbablePrimes.StartAsync(CancellationToken.None));
            await Task.WhenAll(tasks.ToArray());
            
            // Start the RPC server listener
            Log.Information("Launching server listeners...");
            var rpcServer = new RpcServerListener(new Remote());
            var rpcServerTask = rpcServer.StartAsync(config.RpcNetwork.Port, config.RpcNetwork.IPAddress);

            // Start the game server listener
            var server = new Server(config);
            var serverTask = server.StartAsync(config.GameNetwork.Port, config.GameNetwork.IPAddress);

            // Output all clear and wait for user input
            LogEvents.For(typeof(Program), LogEvents.ServerStart).Information(
                "Listening for new connections on {IPAddress}:{Port}",
                config.GameNetwork.IPAddress, config.GameNetwork.Port);
            
            await rpcServerTask;
            await serverTask;
        }
    }
}

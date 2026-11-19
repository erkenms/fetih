namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Network.Sockets;
    using Comet.Game.Database;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.RPC;
    using Comet.Network.Security;
    using Serilog;

    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            Console.Title = "Comet, Game Server";
            Console.WriteLine();
            Console.WriteLine("  Comet: Game Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine("  Patch 5619");
            Console.WriteLine();

            var config = new ServerConfiguration(args);
            LogSetup.Configure("game", config.Logging);
            PacketTrace.Configure(config.Logging?.PacketTrace);
            if (!config.Valid) 
            {
                Log.Error("Invalid server configuration file");
                Log.CloseAndFlush();
                return;
            }

            Log.Information("Initializing server...");
            MsgConnect.StrictAuthentication = config.Authentication.StrictAuthPass;
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Log.Error("Invalid database configuration");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Maps = new MapStore(config.Maps.ResolveClientDirectory());
                Kernel.World = new WorldMaps<Client>(Kernel.Maps);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read map data from {Directory}; set Maps:ClientDirectory " +
                    "to a copy of the 5619 client", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            var tasks = new List<Task>();
            tasks.Add(Kernel.Services.Randomness.StartAsync(CancellationToken.None));
            tasks.Add(DiffieHellman.ProbablePrimes.StartAsync(CancellationToken.None));
            await Task.WhenAll(tasks.ToArray());
            
            Log.Information("Launching server listeners...");
            var rpcServer = new RpcServerListener(new Remote());
            var rpcServerTask = rpcServer.StartAsync(config.RpcNetwork.Port, config.RpcNetwork.IPAddress);

            var server = new Server(config);
            var serverTask = server.StartAsync(config.GameNetwork.Port, config.GameNetwork.IPAddress);

            LogEvents.For(typeof(Program), LogEvents.ServerStart).Information(
                "Listening for new connections on {IPAddress}:{Port}",
                config.GameNetwork.IPAddress, config.GameNetwork.Port);
            
            await rpcServerTask;
            await serverTask;
        }
    }
}

namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Network.Sockets;
    using Comet.Game.Database;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Npcs;
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
                Kernel.Ground = new WorldMaps<GroundItem>(Kernel.Maps);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read map data from {Directory}; set Maps:ClientDirectory " +
                    "to a copy of the 5619 client", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.ItemTypes = ItemTypeTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.ItemTypesLoaded).Information(
                    "Loaded {ItemTypeCount} item types ({SkippedCount} records skipped)",
                    Kernel.ItemTypes.Count, Kernel.ItemTypes.Skipped);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/itemtype.dat from {Directory}",
                    config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Portals = new PortalTable(PortalTable.Load(DataFiles.PathOf("portals.json")));
                LogEvents.For(typeof(Program), LogEvents.PortalsLoaded).Information(
                    "Loaded {PortalCount} portals", Kernel.Portals.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/portals.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json"));
                var placed = npcs.Where(n => Kernel.Maps.Exists(n.MapID)).ToList();
                Kernel.Npcs = new NpcTable(placed);
                LogEvents.For(typeof(Program), LogEvents.NpcsLoaded).Information(
                    "Loaded {NpcCount} NPCs on {MapCount} maps ({SkippedCount} on maps the client doesn't have)",
                    Kernel.Npcs.Count, Kernel.Npcs.MapIDs.Count(), npcs.Count - placed.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/npcs.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                var teleporters = Teleporters.Load(DataFiles.PathOf("teleporters.json"));
                var nowhere = teleporters.SelectMany(t => t.Destinations.Select(d => (t.NpcID, d)))
                    .Where(x => !Kernel.Maps.Exists(x.d.MapID)).ToList();
                foreach (var (npcID, destination) in nowhere)
                    Log.Warning("Teleporter {NpcId} leads to map {MapId}, which the client doesn't have",
                        npcID, destination.MapID);
                Kernel.Teleporters = new Teleporters(teleporters);
                LogEvents.For(typeof(Program), LogEvents.TeleportersLoaded).Information(
                    "Loaded {TeleporterCount} teleporters", Kernel.Teleporters.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/teleporters.json");
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Shops = ShopTable.Load(config.Maps.ResolveClientDirectory());
                LogEvents.For(typeof(Program), LogEvents.ShopsLoaded).Information(
                    "Loaded {ShopCount} shops", Kernel.Shops.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read ini/Shop.dat from {Directory}", config.Maps.ResolveClientDirectory());
                Log.CloseAndFlush();
                return;
            }

            try
            {
                Kernel.Experience = new ExperienceTable(ExperienceTable.Load(DataFiles.PathOf("levels.json")));
                Log.Information("Loaded experience for {LevelCount} levels; the level cap is {MaxLevel}",
                    Kernel.Experience.Count, Kernel.Experience.MaxLevel);
            }
            catch (Exception e)
            {
                Log.Error(e, "Can't read Data/levels.json");
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
            
            Ground.Lifetime = TimeSpan.FromSeconds(Math.Max(1, config.Ground.ExpireSeconds));
            int lost = await ItemsRepository.DeleteGroundAsync((byte)ItemPosition.Ground);
            if (lost > 0)
                Log.Information("Deleted {Count} items left on the ground by the last run", lost);
            _ = Ground.RunExpiryAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

            var saveInterval = TimeSpan.FromSeconds(Math.Max(1, config.Saving.IntervalSeconds));
            _ = SaveLoop.RunAsync(saveInterval, CancellationToken.None);

            await rpcServerTask;
            await serverTask;
        }
    }
}

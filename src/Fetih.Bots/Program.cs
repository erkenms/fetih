namespace Fetih.Bots
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Account.Database;
    using Fetih.Game.World;
    using Microsoft.Extensions.Configuration;

    // Runs groups of bots that log in and play like players, for trying the game's features
    // with others around and for load tests: dotnet run -- --Groups:0:Count=300
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("Fetih.Bots.config", optional: false)
                .AddEnvironmentVariables("FETIH_BOTS_")
                .AddCommandLine(args)
                .Build()
                .Get<BotsConfiguration>();
            var groups = config.Groups.Where(g => g.Count > 0).ToList();
            if (groups.Count == 0)
            {
                Console.Error.WriteLine("No bots to run: give a group a Count");
                return 1;
            }

            ServerDbContext.Configuration = new ServerConfiguration.DatabaseConfiguration
            {
                Hostname = config.AccountDatabase.Hostname, Schema = config.AccountDatabase.Schema,
                Username = config.AccountDatabase.Username, Password = config.AccountDatabase.Password,
            };
            var maps = LoadMaps(config.ClientDirectory);

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

            var stats = new BotStats();
            var bots = new List<Bot>();
            foreach (var group in groups)
            {
                var grid = maps?.Exists(group.Map) == true ? maps.Get(group.Map) : null;
                for (int i = 0; i < group.Count; i++)
                    bots.Add(new Bot(bots.Count + 1, config, group, grid, stats));
                Console.WriteLine($"{group.Count} bots to {group.Behavior} on map {group.Map} at {group.X},{group.Y}");
            }

            // Logins spread over the time given, so a big run doesn't arrive all at once
            var spread = TimeSpan.FromSeconds(Math.Max(0, config.LoginSpreadSeconds));
            var running = bots.Select((bot, i) => Task.Run(async () =>
            {
                try { await Task.Delay(spread * i / bots.Count, stop.Token); }
                catch (OperationCanceledException) { return; }
                await bot.RunAsync(stop.Token);
            })).ToList();

            var every = TimeSpan.FromSeconds(Math.Max(1, config.ReportSeconds));
            var last = DateTime.UtcNow;
            while (!stop.IsCancellationRequested)
            {
                try { await Task.Delay(every, stop.Token); }
                catch (OperationCanceledException) { break; }
                var now = DateTime.UtcNow;
                Console.WriteLine($"{now:HH:mm:ss} {stats.Report(bots.Count, now - last)}");
                last = now;
            }

            Console.WriteLine("Logging the bots out");
            await Task.WhenAll(running);
            return 0;
        }

        // Without the client's maps bots move blind, and the server turns back what it refuses
        private static MapStore LoadMaps(string clientDirectory)
        {
            if (string.IsNullOrEmpty(clientDirectory))
                return null;
            var path = Path.GetFullPath(clientDirectory, AppContext.BaseDirectory);
            try { return new MapStore(path); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                Console.Error.WriteLine($"No client maps in {path}; bots will move without them");
                return null;
            }
        }
    }
}

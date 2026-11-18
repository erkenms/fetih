namespace Comet.Account
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Account.Database;
    using Comet.Account.Database.Repositories;
    using Comet.Core.Logging;
    using Serilog;

    /// <summary>
    /// The account server accepts clients and authenticates players from the client's 
    /// login screen. If the player enters valid account credentials, then the server
    /// will send login details to the game server and disconnect the client. The client
    /// will reconnect to the game server with an access token from the account server.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // Copyright notice may not be commented out. If adding your own copyright or
            // claim of ownership, you may include a second copyright above the existing
            // copyright. Do not remove completely to comply with software license. The
            // project name and version may be removed or changed.
            Console.Title = "Comet, Account Server";
            Console.WriteLine();
            Console.WriteLine("  Comet: Account Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine("  Patch 5619");
            Console.WriteLine();

            // "set-password <username> <password>" sets a password and exits
            bool setPassword = args.Length > 0 && args[0] == "set-password";
            if (setPassword && args.Length < 3)
            {
                Console.WriteLine("  Usage: Comet.Account set-password <username> <password>");
                return;
            }

            // Read configuration file and command-line arguments
            var config = new ServerConfiguration(setPassword ? args[3..] : args);
            LogSetup.Configure("account", config.Logging);
            if (!config.Valid) 
            {
                Log.Error("Invalid server configuration file");
                Log.CloseAndFlush();
                return;
            }

            // Initialize the database
            Log.Information("Initializing server...");
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Log.Error("Invalid database configuration");
                Log.CloseAndFlush();
                return;
            }

            if (setPassword)
            {
                await Program.SetPasswordAsync(args[1], args[2]);
                Log.CloseAndFlush();
                return;
            }

            // Recover caches from the database
            var tasks = new List<Task>();
            tasks.Add(RealmsRepository.LoadAsync());
            await Task.WhenAll(tasks.ToArray());
            
            // Start background services
            tasks = new List<Task>();
            tasks.Add(Kernel.Services.Randomness.StartAsync(CancellationToken.None));
            await Task.WhenAll(tasks.ToArray());
            
            // Start the server listener
            Log.Information("Launching server listener...");
            var server = new Server(config);
            var serverTask = server.StartAsync(config.Network.Port, config.Network.IPAddress);

            // Output all clear and wait for user input
            LogEvents.For(typeof(Program), LogEvents.ServerStart).Information(
                "Listening for new connections on {IPAddress}:{Port}",
                config.Network.IPAddress, config.Network.Port);
            
            await serverTask;
        }

        /// <summary>
        /// Sets an account's password for the SRP6 login used from patch 5619. Accounts
        /// from older patches need this once: their password hashes can't be converted.
        /// </summary>
        /// <param name="username">Username of the account</param>
        /// <param name="password">New password</param>
        private static async Task SetPasswordAsync(string username, string password)
        {
            if (await AccountsRepository.SetPasswordAsync(username, password))
                Log.Information("Password set for {Username}", username);
            else
                Log.Error("No account named {Username}", username);
        }
    }
}

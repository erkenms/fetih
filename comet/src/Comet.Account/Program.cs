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
        private static async Task<int> Main(string[] args)
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

            // "create-account <email> <password>" and "set-password <email> <password>"
            // change the database and exit instead of starting the server
            string command = args.Length > 0 &&
                (args[0] == "create-account" || args[0] == "set-password") ? args[0] : null;
            if (command != null && args.Length < 3)
            {
                Console.WriteLine("  Usage: Comet.Account create-account <email> <password>");
                Console.WriteLine("         Comet.Account set-password <email> <password>");
                return 2;
            }

            // Read configuration file and command-line arguments
            var config = new ServerConfiguration(command != null ? args[3..] : args);
            LogSetup.Configure("account", config.Logging);
            if (!config.Valid) 
            {
                Log.Error("Invalid server configuration file");
                Log.CloseAndFlush();
                return 1;
            }

            // Initialize the database
            Log.Information("Initializing server...");
            ServerDbContext.Configuration = config.Database;
            if (!ServerDbContext.Ping())
            {
                Log.Error("Invalid database configuration");
                Log.CloseAndFlush();
                return 1;
            }

            if (command != null)
            {
                int exitCode = await Program.RunCommandAsync(command, args[1], args[2]);
                Log.CloseAndFlush();
                return exitCode;
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
            return 0;
        }

        /// <summary>
        /// Runs a command-line account command. These are the only ways to create an
        /// account or set a password, since the SRP6 verifier can't be computed in SQL.
        /// </summary>
        /// <param name="command">create-account or set-password</param>
        /// <param name="username">Account name (email address)</param>
        /// <param name="password">Password, as it will be typed in the client</param>
        /// <returns>Process exit code: 0 on success, 1 on failure.</returns>
        private static async Task<int> RunCommandAsync(
            string command, string username, string password)
        {
            if (command == "create-account")
            {
                var (account, _) = await AccountsRepository.CreateAsync(username, password);
                return account != null ? 0 : 1;
            }

            string reason = await AccountsRepository.SetPasswordAsync(username, password);
            if (reason == null)
                Log.Information("Password set for {Username}", username);
            else
                Log.Error("Password not set for {Username}: {Reason}", username, reason);
            return reason == null ? 0 : 1;
        }
    }
}

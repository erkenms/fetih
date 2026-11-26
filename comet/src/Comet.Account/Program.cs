namespace Comet.Account
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Account.Database;
    using Comet.Account.Database.Repositories;
    using Comet.Core.Database;
    using Comet.Core.Logging;
    using Comet.Network.Sockets;
    using Serilog;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.Title = "Comet, Account Server";
            Console.WriteLine();
            Console.WriteLine("  Comet: Account Server");
            Console.WriteLine("  Spirited (C) All Rights Reserved");
            Console.WriteLine("  Patch 5619");
            Console.WriteLine();

            string command = args.Length > 0 &&
                (args[0] == "create-account" || args[0] == "set-password") ? args[0] : null;
            if (command != null && args.Length < 3)
            {
                Console.WriteLine("  Usage: Comet.Account create-account <email> <password>");
                Console.WriteLine("         Comet.Account set-password <email> <password>");
                return 2;
            }

            var config = new ServerConfiguration(command != null ? args[3..] : args);
            LogSetup.Configure("account", config.Logging);
            PacketTrace.Configure(config.Logging?.PacketTrace);
            if (!config.Valid) 
            {
                Log.Error("Invalid server configuration file");
                Log.CloseAndFlush();
                return 1;
            }

            Log.Information("Initializing server...");
            ServerDbContext.Configuration = config.Database;
            if (!await Program.MigrateAsync(config.Database))
            {
                Log.CloseAndFlush();
                return 1;
            }
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

            var tasks = new List<Task>();
            tasks.Add(RealmsRepository.LoadAsync());
            await Task.WhenAll(tasks.ToArray());
            
            tasks = new List<Task>();
            tasks.Add(Kernel.Services.Randomness.StartAsync(CancellationToken.None));
            await Task.WhenAll(tasks.ToArray());
            
            Log.Information("Launching server listener...");
            var server = new Server(config);
            var serverTask = server.StartAsync(config.Network.Port, config.Network.IPAddress);

            LogEvents.For(typeof(Program), LogEvents.ServerStart).Information(
                "Listening for new connections on {IPAddress}:{Port}",
                config.Network.IPAddress, config.Network.Port);
            
            await serverTask;
            return 0;
        }

        // Brings the account schema up to date before anything else reads it
        private static async Task<bool> MigrateAsync(ServerConfiguration.DatabaseConfiguration db)
        {
            try
            {
                await SchemaMigrator.ApplyAsync(db.Hostname, db.Schema, db.Username, db.Password,
                    SchemaMigrator.Load(typeof(Program).Assembly));
                return true;
            }
            catch (MigrationException e)
            {
                Log.Error("Schema migration failed: {Message}", e.Message);
                return false;
            }
        }

        // Accounts and passwords are only set here: the SRP6 verifier can't be computed in SQL.
        private static async Task<int> RunCommandAsync(
            string command, string username, string password)
        {
            string reason;
            if (command == "create-account")
                reason = (await AccountsRepository.CreateAsync(username, password)).Error;
            else
            {
                reason = await AccountsRepository.SetPasswordAsync(username, password);
                if (reason == null)
                    Log.Information("Password set for {Username}", username);
                else
                    Log.Error("Password not set for {Username}: {Reason}", username, reason);
            }

            if (reason != null)
                Console.WriteLine("  " + AccountRules.Explain(reason));
            return reason == null ? 0 : 1;
        }
    }
}

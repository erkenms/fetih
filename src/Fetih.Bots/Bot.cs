namespace Fetih.Bots
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Account.Database.Repositories;
    using Fetih.TestClient;
    using Fetih.Game.World;
    using MySqlConnector;

    // One bot: makes its account and character the first time, puts the character on its
    // group's spot, then plays until stopped. A bot that dies, leaves the map or loses its
    // connection logs in again and goes back to its spot.
    public sealed class Bot
    {
        private static readonly (ushort Mesh, ushort Profession)[] Looks =
        {
            (1003, 10), (1004, 10), (2001, 10), (1003, 20), (1004, 20), (2002, 20),
        };

        private readonly int index;
        private readonly BotsConfiguration config;
        private readonly BotsConfiguration.Group group;
        private readonly MapGrid grid;
        private readonly BotStats stats;
        private readonly Random random = new Random();

        public Bot(int index, BotsConfiguration config, BotsConfiguration.Group group, MapGrid grid, BotStats stats)
        {
            this.index = index;
            this.config = config;
            this.group = group;
            this.grid = grid;
            this.stats = stats;
        }

        public string Name => $"{this.config.Prefix}{this.index:D4}";
        private string Username => $"{this.Name}@bots.local".ToLowerInvariant();

        public async Task RunAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await this.PlayOnceAsync(stop);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception e)
                {
                    this.stats.Failed();
                    Console.Error.WriteLine($"{this.Name}: {e.Message}");
                }
                // The server saves a character as it leaves; placing it before then would be undone
                await Delay(TimeSpan.FromSeconds(3 + this.random.Next(5)), stop);
            }
        }

        private async Task PlayOnceAsync(CancellationToken stop)
        {
            await this.EnsureAccountAsync();
            bool placed = await this.PlaceAsync();
            var look = Looks[this.index % Looks.Length];
            var server = this.config.AccountServer;
            await using var player = await Player.LoginAsync(server.Host, server.Port, server.Realm,
                this.Username, this.config.Password, new NewCharacter(this.Name, look.Mesh, look.Profession), server.GameHost);
            // A new character starts in the birth village; the next login is on the spot
            if (!placed)
                return;

            var game = player.Game;
            var world = new BotWorld(player.User.Id, player.Spawn, player.User.HealthPoints, player.Inventory);
            var behavior = Behavior.Create(this.group.Behavior, game, world, this.group, this.grid, this.stats);
            this.stats.Entered();
            try
            {
                DateTime? ghostAt = null, reviveAt = null;
                while (!stop.IsCancellationRequested)
                {
                    if (game.Closed)
                        throw new InvalidOperationException("the server closed the connection");
                    var now = DateTime.UtcNow;
                    foreach (var packet in game.Drain())
                        world.Apply(packet, this.stats, now);

                    if (world.MapId != this.group.Map)
                        return;
                    if (world.Dead)
                    {
                        // The client turns into a ghost once the fall is over, and the Revive
                        // button works after the server's wait
                        ghostAt ??= now + TimeSpan.FromSeconds(3);
                        reviveAt ??= now + TimeSpan.FromSeconds(25);
                        if (now >= ghostAt && ghostAt != DateTime.MaxValue)
                        {
                            await game.BecomeGhostAsync(world.Me);
                            ghostAt = DateTime.MaxValue;
                        }
                        if (now >= reviveAt)
                        {
                            await game.ReviveAsync(world.Me);
                            reviveAt = now + TimeSpan.FromSeconds(5);
                        }
                    }
                    else
                    {
                        ghostAt = reviveAt = null;
                        // Revived in town, or pushed off: it jumps back to its spot first
                        if (!await behavior.GoBackAsync(now))
                            await behavior.TickAsync(now);
                    }
                    await Delay(TimeSpan.FromMilliseconds(90 + this.random.Next(30)), stop);
                }
            }
            finally
            {
                this.stats.Left();
            }
        }

        private async Task EnsureAccountAsync()
        {
            if (await AccountsRepository.FindAsync(this.Username) != null)
                return;
            var (_, error) = await AccountsRepository.CreateAsync(this.Username, this.config.Password);
            if (error != null && error != "username_taken")
                throw new InvalidOperationException($"can't make account {this.Username}: {error}");
        }

        // Puts the character on a free cell of the spot, at least at the group's level; false
        // if it has no character yet
        private async Task<bool> PlaceAsync()
        {
            var (x, y) = this.Spot();
            var db = this.config.GameDatabase;
            await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = db.Hostname, Database = db.Schema, UserID = db.Username, Password = db.Password,
            }.ConnectionString);
            await connection.OpenAsync();
            var command = new MySqlCommand(
                "UPDATE `character` SET MapID = @map, X = @x, Y = @y, Level = GREATEST(Level, @level) WHERE Name = @name",
                connection);
            command.Parameters.AddWithValue("@map", this.group.Map);
            command.Parameters.AddWithValue("@x", x);
            command.Parameters.AddWithValue("@y", y);
            command.Parameters.AddWithValue("@level", this.group.Level);
            command.Parameters.AddWithValue("@name", this.Name);
            await command.ExecuteNonQueryAsync();
            command = new MySqlCommand("SELECT COUNT(*) FROM `character` WHERE Name = @name", connection);
            command.Parameters.AddWithValue("@name", this.Name);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private (int X, int Y) Spot()
        {
            for (int tries = 0; tries < 50; tries++)
            {
                int x = this.group.X + this.random.Next(-this.group.Spread, this.group.Spread + 1);
                int y = this.group.Y + this.random.Next(-this.group.Spread, this.group.Spread + 1);
                if (this.grid == null || this.grid.IsWalkable(x, y))
                    return (x, y);
            }
            return (this.group.X, this.group.Y);
        }

        private static async Task Delay(TimeSpan time, CancellationToken stop)
        {
            try { await Task.Delay(time, stop); }
            catch (OperationCanceledException) { }
        }
    }
}

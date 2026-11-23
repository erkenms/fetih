namespace Comet.Game.Database.Repositories
{
    using System.Threading.Tasks;
    using MySqlConnector;

    // The few writes the game server makes to the account database (/ban and /unban). The
    // account server reads StatusID at login and refuses banned accounts.
    public static class AccountsRepository
    {
        // The account_status table
        public const ushort Registered = 1, Banned = 5;

        public static ServerConfiguration.DatabaseConfiguration Configuration;

        public enum UnbanResult { Unbanned, NotBanned, UnknownAccount }

        public static async Task<bool> BanAsync(uint accountID)
        {
            await using var db = await OpenAsync();
            var command = new MySqlCommand("UPDATE account SET StatusID = @banned WHERE AccountID = @id", db);
            command.Parameters.AddWithValue("@banned", Banned);
            command.Parameters.AddWithValue("@id", accountID);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public static async Task<UnbanResult> UnbanAsync(string username)
        {
            await using var db = await OpenAsync();
            var update = new MySqlCommand(
                "UPDATE account SET StatusID = @registered WHERE Username = @name AND StatusID = @banned", db);
            update.Parameters.AddWithValue("@registered", Registered);
            update.Parameters.AddWithValue("@banned", Banned);
            update.Parameters.AddWithValue("@name", username);
            if (await update.ExecuteNonQueryAsync() > 0)
                return UnbanResult.Unbanned;

            var exists = new MySqlCommand("SELECT COUNT(*) FROM account WHERE Username = @name", db);
            exists.Parameters.AddWithValue("@name", username);
            return System.Convert.ToInt64(await exists.ExecuteScalarAsync()) > 0
                ? UnbanResult.NotBanned : UnbanResult.UnknownAccount;
        }

        private static async Task<MySqlConnection> OpenAsync()
        {
            var db = new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = Configuration.Hostname,
                Database = Configuration.Schema,
                UserID = Configuration.Username,
                Password = Configuration.Password,
            }.ConnectionString);
            await db.OpenAsync();
            return db;
        }
    }
}

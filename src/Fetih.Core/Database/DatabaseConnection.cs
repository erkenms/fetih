namespace Fetih.Core.Database
{
    using MySqlConnector;

    public static class DatabaseConnection
    {
        public static string Build(
            string hostname,
            string schema,
            string username,
            string password,
            bool allowUserVariables = false,
            bool pooling = true)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = hostname ?? "",
                UserID = username ?? "",
                AllowUserVariables = allowUserVariables,
                Pooling = pooling,
            };

            if (!string.IsNullOrEmpty(hostname) && hostname.StartsWith('/'))
                builder.ConnectionProtocol = MySqlConnectionProtocol.Unix;

            if (!string.IsNullOrEmpty(schema))
                builder.Database = schema;

            if (!string.IsNullOrEmpty(password))
                builder.Password = password;

            return builder.ConnectionString;
        }
    }
}

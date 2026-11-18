namespace Comet.Network.Sockets
{
    /// <summary>
    /// Stable reason values for why a connection ended, written to the
    /// "reason" field of net.disconnect and session.logout. Part of the log contract
    /// (doc/Logging.md): never rename, only add.
    /// </summary>
    public static class DisconnectReasons
    {
        /// <summary>The client closed the connection cleanly (e.g. quit the game).</summary>
        public const string ClientClosed = "client_closed";

        /// <summary>The connection was reset or aborted (client crashed or was killed).</summary>
        public const string Reset = "reset";

        /// <summary>No data was received within the receive timeout (network drop, frozen client).</summary>
        public const string Timeout = "timeout";

        /// <summary>Any other socket error.</summary>
        public const string SocketError = "socket_error";

        /// <summary>The client sent data that could not be split into packets.</summary>
        public const string ProtocolError = "protocol_error";

        /// <summary>The key exchange failed, or the first packet after it was unreadable.</summary>
        public const string HandshakeFailed = "handshake_failed";

        /// <summary>Server-side disconnect without a more specific reason.</summary>
        public const string Server = "server";

        /// <summary>The server is shutting down.</summary>
        public const string Shutdown = "shutdown";

        /// <summary>Login rejected (bad credentials, invalid token, realm offline).</summary>
        public const string AuthFailed = "auth_failed";

        /// <summary>Another login for the same account took over this session.</summary>
        public const string Replaced = "replaced";

        /// <summary>The account server finished its job and redirected the client.</summary>
        public const string Redirected = "redirected";
    }
}

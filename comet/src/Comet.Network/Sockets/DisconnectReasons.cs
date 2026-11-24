namespace Comet.Network.Sockets
{
    public static class DisconnectReasons
    {
        public const string ClientClosed = "client_closed";

        public const string Reset = "reset";

        public const string Timeout = "timeout";

        public const string SocketError = "socket_error";

        public const string ProtocolError = "protocol_error";

        public const string HandshakeFailed = "handshake_failed";

        public const string Server = "server";

        public const string Shutdown = "shutdown";

        public const string AuthFailed = "auth_failed";

        public const string Replaced = "replaced";

        public const string Redirected = "redirected";

        public const string Kicked = "kicked";

        public const string Banned = "banned";

        public const string Deleted = "deleted";
    }
}

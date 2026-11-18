namespace Comet.Core.Logging
{
    using System;
    using Serilog;

    /// <summary>
    /// Catalog of stable event names. Event names and their fields are a public
    /// contract for log consumers (dashboards, scripts) and are documented in
    /// doc/Logging.md. Never rename an event or field; add new ones instead.
    /// </summary>
    public static class LogEvents
    {
        // Server lifecycle
        public const string ServerStart = "server.start";
        public const string ServerStop = "server.stop";

        // Network connections (both servers)
        public const string NetConnect = "net.connect";
        public const string NetDisconnect = "net.disconnect";
        public const string NetRejected = "net.rejected";
        public const string RpcConnected = "rpc.connected";

        // Account server
        public const string AccountLogin = "account.login";
        public const string AccountLoginFailed = "account.login_failed";
        public const string AccountCreated = "account.created";
        public const string AccountCreateFailed = "account.create_failed";

        // Game server sessions
        public const string GameAuthFailed = "game.auth_failed";
        public const string SessionLogin = "session.login";
        public const string SessionLogout = "session.logout";
        public const string SessionReplaced = "session.replaced";
        public const string CharacterCreated = "character.created";
        public const string CharacterCreateFailed = "character.create_failed";
        public const string CharacterRelocated = "character.relocated";
        public const string CharacterSaved = "character.saved";

        // World
        public const string MapIndexLoaded = "map.index_loaded";
        public const string MapLoaded = "map.loaded";
        public const string MapLoadFailed = "map.load_failed";

        // Movement
        public const string MoveWalk = "move.walk";
        public const string MoveJump = "move.jump";
        public const string MoveRejected = "move.rejected";

        // Packets
        public const string PacketUnhandled = "packet.unhandled";
        public const string PacketError = "packet.error";
        public const string PacketIn = "packet.in";
        public const string PacketOut = "packet.out";

        /// <summary>
        /// Returns the global logger tagged with an event name and source class:
        /// <c>LogEvents.For&lt;Server&gt;(LogEvents.NetConnect).Information(...)</c>.
        /// Resolved on each call so it always uses the configured global logger.
        /// </summary>
        public static ILogger For<TSource>(string eventName) =>
            Log.ForContext<TSource>().ForContext("Event", eventName);

        /// <summary>Returns the global logger tagged with an event name.</summary>
        public static ILogger For(Type source, string eventName) =>
            Log.ForContext(source).ForContext("Event", eventName);
    }
}

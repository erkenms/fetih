namespace Comet.Game.States
{
    using System;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.World;
    using Comet.Network.Security;
    using Comet.Network.Sockets;
    using Comet.Shared.Models;

    /// <summary>
    /// Client encapsules the accepted client socket's actor and game server state.
    /// The class should be initialized by the server's Accepted method and returned
    /// to be passed along to the Receive loop and kept alive. Contains all world
    /// interactions with the player.
    /// </summary>
    public sealed class Client : TcpServerActor, IMapObject
    {
        // Fields and Properties 
        public Character Character = null;
        public Creation Creation = null;
        public DiffieHellman DiffieHellman = null;

        // Login token this connection authenticated with, and what it was issued for.
        // Kept so the client can repeat MsgConnect on the same connection.
        public ulong AuthenticationToken { get; set; }
        public TransferAuthArgs Authentication { get; set; }

        // When the character entered the game (set on session.login)
        public DateTime? LoggedInAt { get; set; }

        // Client unique identifier
        public uint ID => Character?.CharacterID ?? 0;
        public uint Identity => this.ID;

        // The map the character stands on (set when entering the game)
        public GameMap<Client> Map { get; set; }

        // Logout state: the logout save runs at most once per client
        private readonly object LogoutLock = new object();
        private Task LogoutTask;

        /// <summary>
        /// Instantiates a new instance of <see cref="Client"/> using the Accepted event's
        /// resulting socket and preallocated buffer. Initializes all account server
        /// states, such as the cipher used to decrypt and encrypt data.
        /// </summary>
        /// <param name="socket">Accepted remote client socket</param>
        /// <param name="buffer">Preallocated buffer from the server listener</param>
        /// <param name="partition">Packet processing partition</param>
        public Client(Socket socket, Memory<byte> buffer, uint partition) 
            : base(socket, buffer, new Cast5Cipher(), partition, "TQServer")
        {
            this.DiffieHellman = new DiffieHellman();
        }

        /// <summary>
        /// Saves the character for logout. Idempotent: every caller awaits the same save,
        /// so a replacing login can wait for the save to finish before it loads the
        /// character, and the disconnect handler cannot start a second, later save.
        /// </summary>
        public Task LogoutAsync()
        {
            lock (this.LogoutLock)
                return this.LogoutTask ??= this.SaveForLogoutAsync();
        }

        private async Task SaveForLogoutAsync()
        {
            var character = this.Character;
            if (character == null)
                return;

            await character.SaveAsync(true);
            LogEvents.For<Client>(LogEvents.CharacterSaved).Debug(
                "Saved {Name} on logout (character {CharacterId}, map {MapId} at {X},{Y})",
                character.Name, character.CharacterID, character.MapID, character.X, character.Y);
        }
    }
}

namespace Comet.Game.Packets
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States;
    using Comet.Network.Packets;
    using Comet.Network.Sockets;
    using Comet.Shared.Models;
    using Org.BouncyCastle.Utilities.Encoders;
    using static Comet.Game.Packets.MsgTalk;

    /// <remarks>Packet Type 1052</remarks>
    /// <summary>
    /// Message containing a connection request to the game server. Contains the player's
    /// access token from the Account server, and the patch and language versions of the
    /// game client.
    /// </summary>
    public sealed class MsgConnect : MsgBase<Client>
    {
        // Static properties from server initialization
        public static bool StrictAuthentication { get; set; }

        // Packet Properties
        public ulong Token { get; set; }
        public ushort Patch { get; set; }
        public string Language { get; set; }
        public string MacAddress { get; set; }
        public int Version { get; set; }

        /// <summary>
        /// Decodes a byte packet into the packet structure defined by this message class. 
        /// Should be invoked to structure data from the client for processing. Decoding
        /// follows TQ Digital's byte ordering rules for an all-binary protocol.
        /// </summary>
        /// <param name="bytes">Bytes from the packet processor or client socket</param>
        /// <remarks>
        /// 5619: from client binary (28 bytes, same offsets as 5187: the two IDs from
        /// MsgConnectEx at 4 and 8 read as one token, patch at 12, language at 14, MAC
        /// address at 16, version at 24).
        /// </remarks>
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Token = reader.ReadUInt64();
            this.Patch = reader.ReadUInt16();
            this.Language = reader.ReadString(2);
            this.MacAddress = Hex.ToHexString(reader.ReadBytes(8));
            this.Version = ParseVersion(reader.ReadInt32());
        }

        /// <summary>
        /// The client writes its version as a number whose decimal digits are binary
        /// digits (e.g. 101 = 5). Falls back to the raw value when it isn't in that form,
        /// instead of throwing and dropping the login.
        /// </summary>
        public static int ParseVersion(int raw)
        {
            var digits = raw.ToString();
            if (raw < 0 || digits.Length > 31 || digits.Any(c => c != '0' && c != '1'))
                return raw;
            return Convert.ToInt32(digits, 2);
        }

        /// <summary>
        /// Returns true if the transferred authentication may be used by a client
        /// connecting from the given IP address. With strict authentication, the game
        /// login must come from the same IP address that passed the account server.
        /// </summary>
        public static bool IsAuthorized(TransferAuthArgs auth, string ipAddress, bool strict) =>
            auth != null && (!strict || auth.IPAddress == ipAddress);

        /// <summary>
        /// Process can be invoked by a packet after decode has been called to structure
        /// packet fields and properties. For the server implementations, this is called
        /// in the packet handler after the message has been dequeued from the server's
        /// <see cref="PacketProcessor"/>.
        /// </summary>
        /// <param name="client">Client requesting packet processing</param>
        public override async Task ProcessAsync(Client client)
        {
            // Validate access token. Remove consumes the token so it can't be reused.
            // 5619: from reference: after creating a character, the client sends
            // MsgConnect again on the same connection, so a connection may repeat the
            // token it already authenticated with.
            TransferAuthArgs auth;
            if (client.Authentication != null && client.AuthenticationToken == this.Token)
            {
                auth = client.Authentication;
                Serilog.Log.ForContext<MsgConnect>().Debug(
                    "Connection {ConnId} repeated its login for account {AccountId}",
                    client.ConnId, auth.AccountID);
            }
            else
            {
                auth = Kernel.Logins.Remove(this.Token.ToString()) as TransferAuthArgs;
            }

            if (!IsAuthorized(auth, client.IPAddress, StrictAuthentication))
            {
                LogEvents.For<MsgConnect>(LogEvents.GameAuthFailed).Warning(
                    "Game login rejected for connection {ConnId} from {IP}: {Reason}",
                    client.ConnId, client.IPAddress,
                    auth == null ? "invalid_token" : "ip_mismatch");
                await client.SendAsync(MsgTalk.LoginInvalid);
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

            // If another client has already connected using this account, take its
            // place, disconnect it, and wait for its logout save before loading the
            // character so this login can't read stale data.
            client.AccountID = auth.AccountID;
            client.Authentication = auth;
            client.AuthenticationToken = this.Token;
            var previous = Kernel.Clients.Replace(auth.AccountID, client);
            if (previous != null && previous != client)
            {
                LogEvents.For<MsgConnect>(LogEvents.SessionReplaced).Information(
                    "Account {AccountId} logged in again from {IP}; closing connection " +
                    "{PreviousConnId} from {PreviousIP}",
                    auth.AccountID, client.IPAddress, previous.ConnId, previous.IPAddress);
                previous.Disconnect(DisconnectReasons.Replaced);
                await previous.LogoutAsync();
            }

            // Check for an existing character
            var character = await CharactersRepository.FindAsync(auth.AccountID);
            if (character == null)
            {
                // Create a new character
                client.Creation = new Creation();
                client.Creation.AccountID = auth.AccountID;
                client.Creation.Token = (uint)this.Token;
                Kernel.Registration.TryAdd(client.Creation.Token, 0);
                Serilog.Log.ForContext<MsgConnect>().Debug(
                    "Account {AccountId} has no character; opening character creation",
                    auth.AccountID);
                await client.SendAsync(MsgTalk.LoginNewRole);
            }
            else
            {
                // Character already exists
                client.Character = new Character(character);
                client.LoggedInAt = DateTime.UtcNow;
                LogEvents.For<MsgConnect>(LogEvents.SessionLogin).Information(
                    "{Name} logged in (connection {ConnId}, account {AccountId}, " +
                    "character {CharacterId}, level {Level}, from {IP}, map {MapId} at {X},{Y})",
                    character.Name, client.ConnId, auth.AccountID, character.CharacterID,
                    character.Level, client.IPAddress, character.MapID, character.X, character.Y);
                await client.SendAsync(MsgTalk.LoginOk);
                await client.SendAsync(new MsgUserInfo(client.Character));
            }            
        }
    }
}
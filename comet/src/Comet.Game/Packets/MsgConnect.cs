namespace Comet.Game.Packets
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using Comet.Network.Sockets;
    using Comet.Shared.Models;
    using Org.BouncyCastle.Utilities.Encoders;
    using static Comet.Game.Packets.MsgTalk;

    public sealed class MsgConnect : MsgBase<Client>
    {
        public static bool StrictAuthentication { get; set; }

        public ulong Token { get; set; }
        public ushort Patch { get; set; }
        public string Language { get; set; }
        public string MacAddress { get; set; }
        public int Version { get; set; }

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

        // The client writes its version as decimal digits that are binary digits (101 = 5).
        public static int ParseVersion(int raw)
        {
            var digits = raw.ToString();
            if (raw < 0 || digits.Length > 31 || digits.Any(c => c != '0' && c != '1'))
                return raw;
            return Convert.ToInt32(digits, 2);
        }

        public static bool IsAuthorized(TransferAuthArgs auth, string ipAddress, bool strict) =>
            auth != null && (!strict || auth.IPAddress == ipAddress);

        public override async Task ProcessAsync(Client client)
        {
            // Once a character is in the game, a repeated login would reload it from the
            // database and drop any changes not saved yet, so ignore it.
            if (client.Character != null)
            {
                Serilog.Log.ForContext<MsgConnect>().Warning(
                    "Connection {ConnId} sent MsgConnect while {Name} is already in the game; ignored",
                    client.ConnId, client.Character.Name);
                return;
            }

            // Remove consumes the token. After creating a character the client sends
            // MsgConnect again on the same connection, repeating the token it used.
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

            client.LoggedIn();

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

            var character = await CharactersRepository.FindAsync(auth.AccountID);
            if (character == null)
            {
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
                // Character already exists: check where it stands, then add it to the
                // world (it appears on the map when the client finishes logging in)
                client.Character = new Character(character);
                var items = await ItemsRepository.LoadAsync(character.CharacterID);
                client.Inventory = new Inventory(items.Where(i => i.Position == (byte)ItemPosition.Inventory));
                client.Equipment = new Equipment(items.Where(i => Equipment.IsEquipped(i.Position)));
                await Social.Relationships.LoadAsync(client);
                if (!ResolveMap(client))
                {
                    await client.SendAsync(MsgTalk.LoginInvalid);
                    client.Disconnect(DisconnectReasons.Server);
                    return;
                }

                var entered = client.Character;
                client.LoggedInAt = DateTime.UtcNow;
                Kernel.Players.Add(entered.CharacterID, entered.Name, client);
                LogEvents.For<MsgConnect>(LogEvents.SessionLogin).Information(
                    "{Name} logged in (connection {ConnId}, account {AccountId}, " +
                    "character {CharacterId}, level {Level}, from {IP}, map {MapId} at {X},{Y}; " +
                    "{Online} online)",
                    entered.Name, client.ConnId, auth.AccountID, entered.CharacterID,
                    entered.Level, client.IPAddress, entered.MapID, entered.X, entered.Y,
                    Kernel.Players.Count);
                await client.SendAsync(MsgTalk.LoginOk);
                await client.SendAsync(new MsgUserInfo(client.Character));
            }            
        }

        private static bool ResolveMap(Client client)
        {
            var character = client.Character;
            if (!Kernel.World.TryGet(character.MapID, out var map) ||
                !map.Grid.IsWalkable(character.X, character.Y))
            {
                LogEvents.For<MsgConnect>(LogEvents.CharacterRelocated).Warning(
                    "{Name} was saved at map {FromMapId} at {FromX},{FromY}, where they can't " +
                    "stand ({Reason}); moving them to the start position",
                    character.Name, character.MapID, character.X, character.Y,
                    map == null ? "unknown_map" : "blocked_cell");
                character.MapID = StartPosition.MapID;
                character.X = StartPosition.X;
                character.Y = StartPosition.Y;
                if (!Kernel.World.TryGet(character.MapID, out map))
                {
                    Serilog.Log.ForContext<MsgConnect>().Error(
                        "Start map {MapId} can't be loaded; {Name} can't enter the game",
                        character.MapID, character.Name);
                    return false;
                }
            }

            client.Map = map;
            return true;
        }
    }
}
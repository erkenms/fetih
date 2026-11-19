namespace Comet.Account.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Account.Database.Models;
    using Comet.Account.States;
    using Comet.Core.Logging;
    using Comet.Network.Packets;
    using Comet.Network.Sockets;
    using Comet.Shared.Models;
    using static Comet.Account.Packets.MsgConnectEx;

    /// <remarks>Packet Type 1214</remarks>
    /// <summary>
    /// Message containing the client's answer to <see cref="MsgLoginChallengeS"/>: its
    /// SRP6 public key A and the proof M1. If the proof is valid, the server sends the
    /// client to the game server with <see cref="MsgConnectEx"/>.
    /// </summary>
    public sealed class MsgLoginProofC : MsgBase<Client>
    {
        public byte[] ClientPublicKey { get; private set; }
        public byte[] ClientProof { get; private set; }

        /// <summary>
        /// Decodes a byte packet into the packet structure defined by this message class. 
        /// Should be invoked to structure data from the client for processing. Decoding
        /// follows TQ Digital's byte ordering rules for an all-binary protocol.
        /// </summary>
        /// <remarks>
        /// 5619: confirmed in VM (262 bytes: A as a length byte at 4 and 128 byte slot
        /// at 5, M1 as a length byte at 133 and slot at 134; both little-endian).
        /// </remarks>
        /// <param name="bytes">Bytes from the packet processor or client socket</param>
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.ClientPublicKey = ReadField(reader);
            this.ClientProof = ReadField(reader);
        }

        private static byte[] ReadField(PacketReader reader)
        {
            int length = reader.ReadByte();
            var slot = reader.ReadBytes(MsgLoginChallengeS.FieldSize);
            if (length > slot.Length)
                throw new ArgumentOutOfRangeException(nameof(length),
                    $"SRP6 field length {length} is larger than its slot");
            return slot[..length];
        }

        /// <summary>
        /// Process can be invoked by a packet after decode has been called to structure
        /// packet fields and properties. For the server implementations, this is called
        /// in the packet handler after the message has been dequeued from the server's
        /// <see cref="PacketProcessor"/>.
        /// </summary>
        /// <param name="client">Client requesting packet processing</param>
        public override async Task ProcessAsync(Client client)
        {
            // A proof is only expected after a challenge
            var srp = client.Srp;
            client.Srp = null;
            if (srp == null)
            {
                Serilog.Log.ForContext<MsgLoginProofC>().Warning(
                    "Connection {ConnId} from {IP} sent a login proof without a challenge",
                    client.ConnId, client.IPAddress);
                client.Disconnect(DisconnectReasons.ProtocolError);
                return;
            }

            var username = client.Account.Username;
            if (!srp.Verify(client.UsernameBytes, this.ClientPublicKey, this.ClientProof))
            {
                if (srp.ShortSessionSecret)
                    Serilog.Log.ForContext<MsgLoginProofC>().Debug(
                        "SRP6 secret for {Username} was shorter than 32 bytes; the client " +
                        "can't compute a valid proof for it, so the password may be right",
                        username);
                LogEvents.For<MsgLoginProofC>(LogEvents.AccountLoginFailed).Warning(
                    "Login failed for {Username} from {IP}: {Reason}",
                    username, client.IPAddress, "bad_password");
                await client.SendAsync(new MsgConnectEx(RejectionCode.InvalidPassword));
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

            // Refuse banned and locked accounts. This is checked after the proof, so the
            // account's standing is only revealed to someone who knows the password.
            var status = (AccountStatus)client.Account.StatusID;
            if (status == AccountStatus.Banned || status == AccountStatus.Locked)
            {
                bool banned = status == AccountStatus.Banned;
                LogEvents.For<MsgLoginProofC>(LogEvents.AccountLoginFailed).Warning(
                    "Login failed for {Username} from {IP}: {Reason}",
                    username, client.IPAddress, banned ? "account_banned" : "account_locked");
                await client.SendAsync(new MsgConnectEx(
                    banned ? RejectionCode.AccountBanned : RejectionCode.AccountLocked));
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

            // Connect to the game server
            Database.Models.DbRealm server;
            if (!Kernel.Realms.TryGetValue(client.Realm, out server) || !server.Rpc.Online)
            {
                LogEvents.For<MsgLoginProofC>(LogEvents.AccountLoginFailed).Warning(
                    "Login failed for {Username} from {IP}: {Reason} ({Realm})",
                    username, client.IPAddress, "realm_offline", client.Realm);
                await client.SendAsync(new MsgConnectEx(RejectionCode.ServerDown));
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

            // Get an access token from the server
            var args = new TransferAuthArgs();
            args.AccountID = client.Account.AccountID;
            args.AuthorityID = client.Account.AuthorityID;
            args.AuthorityName = client.Account.Authority.AuthorityName;
            args.IPAddress = client.IPAddress;

            ulong token = await server.Rpc.CallAsync<ulong>("TransferAuth", args);
            await client.SendAsync(new MsgConnectEx(server.GameIPAddress, server.GamePort, token));
            LogEvents.For<MsgLoginProofC>(LogEvents.AccountLogin).Information(
                "{Username} (account {AccountId}) logged in from {IP}, redirected to realm {Realm}",
                username, client.Account.AccountID, client.IPAddress, client.Realm);
            client.SetDisconnectReason(DisconnectReasons.Redirected);
        }
    }
}

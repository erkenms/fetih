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

    public sealed class MsgLoginProofC : MsgBase<Client>
    {
        public byte[] ClientPublicKey { get; private set; }
        public byte[] ClientProof { get; private set; }

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

        public override async Task ProcessAsync(Client client)
        {
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

            // Checked after the proof, so an account's standing is only revealed to someone
            // who knows the password.
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

            if (client.Account.AuthorityID < server.AuthorityID)
            {
                LogEvents.For<MsgLoginProofC>(LogEvents.AccountLoginFailed).Warning(
                    "Login failed for {Username} from {IP}: {Reason} ({Realm})",
                    username, client.IPAddress, "realm_authority", client.Realm);
                await client.SendAsync(new MsgConnectEx(RejectionCode.ServerLocked));
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

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

namespace Comet.Account.Packets
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Comet.Account.Database.Repositories;
    using Comet.Account.States;
    using Comet.Core.Logging;
    using Comet.Network.Packets;
    using Comet.Network.Sockets;
    using static Comet.Account.Packets.MsgConnectEx;

    public sealed class MsgAccountSRP6 : MsgBase<Client>
    {
        public byte[] UsernameBytes { get; private set; }
        public string Username { get; private set; }
        public string Realm { get; private set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            reader.BaseStream.Seek(8, SeekOrigin.Begin);
            var username = reader.ReadBytes(128);
            int end = Array.IndexOf(username, (byte)0);
            this.UsernameBytes = username[..(end < 0 ? username.Length : end)];
            this.Username = System.Text.Encoding.Latin1.GetString(this.UsernameBytes);
            this.Realm = reader.ReadString(16);
        }

        public override async Task ProcessAsync(Client client)
        {
            client.Account = await AccountsRepository.FindAsync(this.Username).ConfigureAwait(false);
            client.AccountID = client.Account?.AccountID ?? 0;
            client.Srp = client.Account != null
                ? AccountsRepository.BeginLogin(client.Account) : null;
            if (client.Srp == null)
            {
                LogEvents.For<MsgAccountSRP6>(LogEvents.AccountLoginFailed).Warning(
                    "Login failed for {Username} from {IP}: {Reason}",
                    this.Username, client.IPAddress,
                    client.Account == null ? "unknown_account" : "password_not_set");
                await client.SendAsync(new MsgConnectEx(RejectionCode.InvalidPassword));
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

            client.UsernameBytes = this.UsernameBytes;
            client.Realm = this.Realm;
            Serilog.Log.ForContext<MsgAccountSRP6>().Debug(
                "Sending SRP6 challenge to {Username} (account {AccountId}) on connection {ConnId}",
                this.Username, client.Account.AccountID, client.ConnId);
            await client.SendAsync(new MsgLoginChallengeS(client.Srp));
        }
    }
}

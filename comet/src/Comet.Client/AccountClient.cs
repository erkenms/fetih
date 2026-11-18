namespace Comet.Client
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Network.Security;

    /// <summary>The account server's answer to a login.</summary>
    /// <param name="Accepted">True if the server redirected the client to a game server</param>
    /// <param name="RejectCode">MsgConnectEx code when rejected (1 wrong password, 10 realm
    /// offline, 12 banned, 22 locked)</param>
    /// <param name="Challenged">True if the server sent an SRP6 challenge</param>
    /// <param name="Token">Game server access token</param>
    /// <param name="GameHost">Game server address</param>
    /// <param name="GamePort">Game server port</param>
    public sealed record LoginResult(
        bool Accepted, uint RejectCode, bool Challenged, ulong Token, string GameHost,
        int GamePort);

    /// <summary>
    /// Test client for the account server: logs in the way the 5619 client does
    /// (MsgAccountSRP6, the SRP6 challenge, MsgLoginProofC, then MsgConnectEx).
    /// </summary>
    public sealed class AccountClient : Connection
    {
        private AccountClient(System.Net.Sockets.TcpClient tcp)
            : base(tcp, new TQClientCipher(), footer: "")
        {
        }

        /// <summary>The seed from MsgEncryptCode, sent by the server on connect.</summary>
        public uint Seed { get; private set; }

        /// <summary>Connects and reads the server's MsgEncryptCode.</summary>
        public static async Task<AccountClient> ConnectAsync(string host, int port)
        {
            var client = new AccountClient(await OpenAsync(host, port));
            client.StartReading();
            var seed = await client.ReceiveAsync()
                ?? throw new InvalidOperationException("Account server closed the connection");
            if (BitConverter.ToUInt16(seed, 2) != PacketTypes.MsgEncryptCode)
                throw new InvalidOperationException("Expected MsgEncryptCode first");
            client.Seed = BitConverter.ToUInt32(seed, 4);
            return client;
        }

        /// <summary>Builds MsgAccountSRP6: account name at 8 (128 bytes), realm at 136.</summary>
        public static byte[] BuildAccountRequest(byte[] username, string realm)
        {
            var packet = Packet.Create(PacketTypes.MsgAccountSRP6, 240);
            Packet.WriteText(packet, 8, username, 128);
            Packet.WriteText(packet, 136, Encoding.ASCII.GetBytes(realm), 16);
            return packet;
        }

        /// <summary>Builds MsgLoginProofC: A at 4 and M1 at 133, each length byte + 128.</summary>
        public static byte[] BuildProof(byte[] publicKey, byte[] proof)
        {
            var packet = Packet.Create(PacketTypes.MsgLoginProofC, 262);
            packet[4] = (byte)publicKey.Length;
            publicKey.CopyTo(packet, 5);
            packet[133] = (byte)proof.Length;
            proof.CopyTo(packet, 134);
            return packet;
        }

        /// <summary>
        /// Logs in with a name and password. Returns the server's answer; if the server
        /// closes the connection without one, the result is a rejection with code 0.
        /// </summary>
        public Task<LoginResult> LoginAsync(string username, string password, string realm) =>
            this.LoginAsync(Encoding.Latin1.GetBytes(username), Encoding.Latin1.GetBytes(password), realm);

        /// <summary>Logs in with raw name and password bytes.</summary>
        public async Task<LoginResult> LoginAsync(byte[] username, byte[] password, string realm)
        {
            await this.SendAsync(BuildAccountRequest(username, realm));
            var reply = await this.ReceiveAsync();
            if (reply == null || BitConverter.ToUInt16(reply, 2) != PacketTypes.MsgLoginChallengeS)
                return ReadConnectEx(reply, challenged: false);

            // MsgLoginChallengeS: B at 4 and the salt at 391, each a length byte and slot
            var serverPublicKey = reply.AsSpan(5, reply[4]).ToArray();
            var salt = reply.AsSpan(392, reply[391]).ToArray();
            var (publicKey, proof, _) = Srp6Client.ComputeProof(
                username, password, salt, serverPublicKey);
            await this.SendAsync(BuildProof(publicKey, proof));
            return ReadConnectEx(await this.ReceiveAsync(), challenged: true);
        }

        /// <summary>
        /// Reads MsgConnectEx. Accept: token halves at 4 and 8, port at 12, IP at 20.
        /// Reject: zero at 4 and the code at 8.
        /// </summary>
        private static LoginResult ReadConnectEx(byte[] packet, bool challenged)
        {
            if (packet == null || BitConverter.ToUInt16(packet, 2) != PacketTypes.MsgConnectEx)
                return new LoginResult(false, 0, challenged, 0, null, 0);
            if (BitConverter.ToUInt32(packet, 4) == 0)
                return new LoginResult(false, BitConverter.ToUInt32(packet, 8), challenged, 0, null, 0);
            return new LoginResult(true, 0, challenged, BitConverter.ToUInt64(packet, 4),
                Packet.ReadText(packet, 20, 32), (int)BitConverter.ToUInt32(packet, 12));
        }
    }
}

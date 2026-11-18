namespace Comet.Client
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Network.Security;
    using Org.BouncyCastle.Math;
    using Org.BouncyCastle.Utilities.Encoders;

    /// <summary>The game server's answer to MsgConnect.</summary>
    /// <param name="Message">MsgTalk text: ANSWER_OK, NEW_ROLE, or why it was refused</param>
    /// <param name="User">The character, when the login went straight into the game</param>
    public sealed record GameLoginResult(string Message, UserInfo User)
    {
        public bool NeedsCharacter => this.Message == "NEW_ROLE";
        public bool InGame => this.User != null;
    }

    /// <summary>
    /// Test client for the game server: does the Diffie-Hellman key exchange and switches
    /// to the agreed CAST5 key, then logs in, creates characters and enters the map the
    /// way the 5619 client does.
    /// </summary>
    public sealed class GameClient : Connection
    {
        private volatile bool HandshakeReceived;

        private GameClient(System.Net.Sockets.TcpClient tcp)
            : base(tcp, new Cast5Cipher(), footer: "TQClient")
        {
        }

        /// <summary>
        /// Connects and completes the key exchange. Afterwards both directions use the
        /// key derived from the shared secret.
        /// </summary>
        public static async Task<GameClient> ConnectAsync(string host, int port)
        {
            var client = new GameClient(await OpenAsync(host, port));
            client.StartReading();
            var handshake = await client.ReceiveAsync()
                ?? throw new InvalidOperationException("Game server closed the connection");
            client.HandshakeReceived = true;

            // Server handshake after 11 bytes of padding: size, padding length, padding,
            // both IVs, then the prime, generator and server public key as hex text
            using var reader = new BinaryReader(new MemoryStream(handshake));
            reader.BaseStream.Seek(15, SeekOrigin.Begin);
            reader.BaseStream.Seek(reader.ReadInt32(), SeekOrigin.Current);
            reader.ReadBytes(reader.ReadInt32());
            reader.ReadBytes(reader.ReadInt32());
            var prime = new BigInteger(ReadHex(reader), 16);
            var generator = new BigInteger(ReadHex(reader), 16);
            var serverKey = new BigInteger(ReadHex(reader), 16);

            var privateKey = new BigInteger(1, RandomNumberGenerator.GetBytes(32));
            var publicKey = generator.ModPow(privateKey, prime);
            var secret = serverKey.ModPow(privateKey, prime);

            // Reply: 7 bytes of padding, the length after them, a padding block, then the
            // client public key as hex text, all under the initial key
            var key = Encoding.ASCII.GetBytes(Hex.ToHexString(publicKey.ToByteArrayUnsigned()).ToUpper());
            var junk = RandomNumberGenerator.GetBytes(12);
            var reply = new MemoryStream();
            var writer = new BinaryWriter(reply);
            writer.Write(RandomNumberGenerator.GetBytes(7));
            writer.Write(4 + 4 + junk.Length + 4 + key.Length + 8);
            writer.Write(junk.Length);
            writer.Write(junk);
            writer.Write(key.Length);
            writer.Write(key);
            writer.Write(Encoding.ASCII.GetBytes("TQClient"));
            await client.SendRawAsync(reply.ToArray());

            client.Cipher.GenerateKeys(new object[] {
                DiffieHellman.DeriveCipherKey(secret.ToByteArrayUnsigned()) });
            return client;
        }

        private static string ReadHex(BinaryReader reader) =>
            Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));

        /// <summary>The handshake isn't framed like other packets: its size is at 11.</summary>
        protected override int FrameLength(byte[] buffer, int count)
        {
            if (this.HandshakeReceived)
                return base.FrameLength(buffer, count);
            return count < 15 ? 0 : 11 + BitConverter.ToInt32(buffer, 11);
        }

        /// <summary>Builds MsgConnect (28 bytes): the access token at 4.</summary>
        public static byte[] BuildConnect(ulong token)
        {
            var packet = Packet.Create(PacketTypes.MsgConnect, 28);
            BitConverter.TryWriteBytes(packet.AsSpan(4), token);
            BitConverter.TryWriteBytes(packet.AsSpan(12), (ushort)5619);
            Encoding.ASCII.GetBytes("En").CopyTo(packet, 14);
            return packet;
        }

        /// <summary>
        /// Builds MsgRegister (124 bytes): name at 24 (16 bytes), body at 72, class at 74,
        /// the access token at 76, MAC address text at 80.
        /// </summary>
        public static byte[] BuildRegister(byte[] name, ushort mesh, ushort profession, uint token)
        {
            var packet = Packet.Create(PacketTypes.MsgRegister, 124);
            Packet.WriteText(packet, 24, name, 16);
            BitConverter.TryWriteBytes(packet.AsSpan(72), mesh);
            BitConverter.TryWriteBytes(packet.AsSpan(74), profession);
            BitConverter.TryWriteBytes(packet.AsSpan(76), token);
            Encoding.ASCII.GetBytes("00.1C.42.00.00.00").CopyTo(packet, 80);
            return packet;
        }

        /// <summary>
        /// Sends MsgConnect and reads the answer. On ANSWER_OK the character follows in
        /// MsgUserInfo. If the server closes the connection first, the message is null.
        /// </summary>
        public async Task<GameLoginResult> LoginAsync(ulong token)
        {
            await this.SendAsync(BuildConnect(token));
            var talk = await this.ReceiveAsync(PacketTypes.MsgTalk);
            if (talk == null)
                return new GameLoginResult(null, null);

            var message = TalkMessage.Parse(talk).Text;
            if (message != "ANSWER_OK")
                return new GameLoginResult(message, null);
            var user = await this.ReceiveAsync(PacketTypes.MsgUserInfo);
            return new GameLoginResult(message, user == null ? null : UserInfo.Parse(user));
        }

        /// <summary>
        /// Sends MsgRegister and returns the server's MsgTalk answer (ANSWER_OK on
        /// success), or null if it closed the connection.
        /// </summary>
        public async Task<string> CreateCharacterAsync(
            string name, ushort mesh, ushort profession, uint token) =>
            await this.CreateCharacterAsync(Encoding.Latin1.GetBytes(name), mesh, profession, token);

        /// <summary>Sends MsgRegister with raw name bytes.</summary>
        public async Task<string> CreateCharacterAsync(
            byte[] name, ushort mesh, ushort profession, uint token)
        {
            await this.SendAsync(BuildRegister(name, mesh, profession, token));
            var talk = await this.ReceiveAsync(PacketTypes.MsgTalk);
            return talk == null ? null : TalkMessage.Parse(talk).Text;
        }

        /// <summary>
        /// Asks to be placed on the map (LoginSpawn) and finishes logging in
        /// (LoginComplete, 251), as the 5619 client does after MsgUserInfo. The server
        /// doesn't answer LoginComplete. Returns the spawn reply: the map in Command and
        /// the position in X and Y.
        /// </summary>
        public async Task<ActionMessage> EnterMapAsync(uint characterId)
        {
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.LoginSpawn));
            var spawn = await this.ReceiveActionAsync(ActionMessage.LoginSpawn);
            await this.SendAsync(ActionMessage.BuildLoginComplete());
            return spawn;
        }

        /// <summary>
        /// Waits for another player to come into view, skipping other packets. Returns
        /// null if they don't appear in time.
        /// </summary>
        public async Task<PlayerSpawn> WaitForSpawnAsync(uint playerId, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
            while (true)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                    return null;
                byte[] packet;
                try { packet = await this.ReceiveAsync(PacketTypes.MsgPlayer, left); }
                catch (TimeoutException) { return null; }
                if (packet == null)
                    return null;
                var spawn = PlayerSpawn.Parse(packet);
                if (spawn.Id == playerId)
                    return spawn;
            }
        }

        /// <summary>
        /// Waits for an entity to be taken off the screen, skipping other packets.
        /// Returns false if that doesn't happen in time.
        /// </summary>
        public async Task<bool> WaitForRemovalAsync(uint entityId, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
            while (true)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                    return false;
                byte[] packet;
                try { packet = await this.ReceiveAsync(PacketTypes.MsgAction, left); }
                catch (TimeoutException) { return false; }
                if (packet == null)
                    return false;
                var action = ActionMessage.Parse(packet);
                if (action.Action == ActionMessage.RemoveEntity && action.CharacterId == entityId)
                    return true;
            }
        }

        private async Task<ActionMessage> ReceiveActionAsync(ushort action)
        {
            while (true)
            {
                var packet = await this.ReceiveAsync(PacketTypes.MsgAction)
                    ?? throw new InvalidOperationException("Game server closed the connection");
                var message = ActionMessage.Parse(packet);
                if (message.Action == action)
                    return message;
            }
        }
    }
}

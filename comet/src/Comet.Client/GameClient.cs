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
        /// Waits for another player to come into view. Returns null if they don't appear
        /// in time.
        /// </summary>
        public async Task<PlayerSpawn> WaitForSpawnAsync(uint playerId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPlayer &&
                PlayerSpawn.Parse(p).Id == playerId, timeout);
            return packet == null ? null : PlayerSpawn.Parse(packet);
        }

        /// <summary>
        /// Waits for an entity to be taken off the screen. Returns false if that doesn't
        /// happen in time.
        /// </summary>
        public async Task<bool> WaitForRemovalAsync(uint entityId, TimeSpan? timeout = null) =>
            await this.WaitForActionAsync(ActionMessage.RemoveEntity, entityId, timeout) != null;

        /// <summary>
        /// Waits for an action about an entity (MsgAction with that action and ID).
        /// Returns null if none arrives in time.
        /// </summary>
        public async Task<ActionMessage> WaitForActionAsync(ushort action, uint entityId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction &&
                ActionMessage.Parse(p) is var a && a.Action == action && a.CharacterId == entityId, timeout);
            return packet == null ? null : ActionMessage.Parse(packet);
        }

        /// <summary>Waits for a step by an entity. Returns null if none arrives in time.</summary>
        public async Task<WalkMessage> WaitForWalkAsync(uint entityId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgWalk &&
                WalkMessage.Parse(p).CharacterId == entityId, timeout);
            return packet == null ? null : WalkMessage.Parse(packet);
        }

        /// <summary>Takes one step in a direction (0 to 7), as the client does.</summary>
        public Task WalkAsync(uint characterId, int direction) =>
            this.SendAsync(WalkMessage.Build(characterId, direction));

        /// <summary>Jumps from one cell to another, as the client does.</summary>
        public Task JumpAsync(uint characterId, int fromX, int fromY, int toX, int toY) =>
            this.SendAsync(ActionMessage.BuildJump(characterId, fromX, fromY, toX, toY));

        /// <summary>Turns to face a direction (0 to 7).</summary>
        public Task TurnAsync(uint characterId, int direction) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Turn, direction: direction));

        /// <summary>Shows an emote (the stance number goes in Command).</summary>
        public Task EmoteAsync(uint characterId, uint stance, int direction) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Emote, command: stance, direction: direction));

        private async Task<byte[]> TryReceiveAsync(Func<byte[], bool> match, TimeSpan? timeout)
        {
            try { return await this.ReceiveAsync(match, timeout); }
            catch (TimeoutException) { return null; }
        }

        private async Task<ActionMessage> ReceiveActionAsync(ushort action)
        {
            var packet = await this.ReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction &&
                ActionMessage.Parse(p).Action == action)
                ?? throw new InvalidOperationException("Game server closed the connection");
            return ActionMessage.Parse(packet);
        }
    }
}

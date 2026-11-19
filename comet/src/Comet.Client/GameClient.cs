namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Network.Security;
    using Org.BouncyCastle.Math;
    using Org.BouncyCastle.Utilities.Encoders;

    public sealed record GameLoginResult(string Message, UserInfo User)
    {
        public bool NeedsCharacter => this.Message == "NEW_ROLE";
        public bool InGame => this.User != null;
    }

    public sealed class GameClient : Connection
    {
        private volatile bool HandshakeReceived;

        private GameClient(System.Net.Sockets.TcpClient tcp)
            : base(tcp, new Cast5Cipher(), footer: "TQClient")
        {
        }

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

        // The handshake isn't framed like other packets: its size is at 11.
        protected override int FrameLength(byte[] buffer, int count)
        {
            if (this.HandshakeReceived)
                return base.FrameLength(buffer, count);
            return count < 15 ? 0 : 11 + BitConverter.ToInt32(buffer, 11);
        }

        public static byte[] BuildConnect(ulong token)
        {
            var packet = Packet.Create(PacketTypes.MsgConnect, 28);
            BitConverter.TryWriteBytes(packet.AsSpan(4), token);
            BitConverter.TryWriteBytes(packet.AsSpan(12), (ushort)5619);
            Encoding.ASCII.GetBytes("En").CopyTo(packet, 14);
            return packet;
        }

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

        public async Task<string> CreateCharacterAsync(
            string name, ushort mesh, ushort profession, uint token) =>
            await this.CreateCharacterAsync(Encoding.Latin1.GetBytes(name), mesh, profession, token);

        public async Task<string> CreateCharacterAsync(
            byte[] name, ushort mesh, ushort profession, uint token)
        {
            await this.SendAsync(BuildRegister(name, mesh, profession, token));
            var talk = await this.ReceiveAsync(PacketTypes.MsgTalk);
            return talk == null ? null : TalkMessage.Parse(talk).Text;
        }

        public async Task<(ActionMessage Spawn, List<ItemInfo> Inventory)> EnterMapAsync(uint characterId)
        {
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.LoginSpawn));
            var spawn = await this.ReceiveActionAsync(ActionMessage.LoginSpawn);

            // Items come before the reply to the inventory step
            var inventory = new List<ItemInfo>();
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.LoginInventory));
            while (true)
            {
                var packet = await this.ReceiveAsync(p =>
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItemInfo ||
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction &&
                    ActionMessage.Parse(p).Action == ActionMessage.LoginInventory)
                    ?? throw new InvalidOperationException("Game server closed the connection");
                if (BitConverter.ToUInt16(packet, 2) == PacketTypes.MsgAction)
                    break;
                inventory.Add(ItemInfo.Parse(packet));
            }

            await this.SendAsync(ActionMessage.BuildLoginComplete());
            return (spawn, inventory);
        }

        public Task EquipAsync(uint itemId, uint position) =>
            this.SendAsync(ItemMessage.Build(itemId, position, ItemMessage.Equip));

        public Task UnequipAsync(uint itemId, uint position) =>
            this.SendAsync(ItemMessage.Build(itemId, position, ItemMessage.Unequip));

        public async Task<ItemMessage> WaitForItemActionAsync(uint action, uint itemId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItem &&
                ItemMessage.Parse(p) is var m && m.Action == action && m.Id == itemId, timeout);
            return packet == null ? null : ItemMessage.Parse(packet);
        }

        public Task DropAsync(uint itemId) =>
            this.SendAsync(ItemMessage.Build(itemId, 0, ItemMessage.Drop));

        public Task DropSilverAsync(uint amount) =>
            this.SendAsync(ItemMessage.Build(amount, 0, ItemMessage.DropSilver));

        public Task PickUpAsync(uint groundId, int x, int y) =>
            this.SendAsync(GroundItemMessage.BuildPick(groundId, x, y));

        public async Task<GroundItemMessage> WaitForGroundItemAsync(
            Func<GroundItemMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgMapItem && match(GroundItemMessage.Parse(p)), timeout);
            return packet == null ? null : GroundItemMessage.Parse(packet);
        }

        public async Task<NpcSpawn> WaitForNpcAsync(uint npcId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgNpcInfo && NpcSpawn.Parse(p).Id == npcId, timeout);
            return packet == null ? null : NpcSpawn.Parse(packet);
        }

        public Task TalkToNpcAsync(uint npcId) => this.SendAsync(NpcSpawn.BuildClick(npcId));

        public Task AnswerNpcAsync(byte option) => this.SendAsync(DialogMessage.BuildAnswer(option));

        // The parts of the next dialog page, up to and including Show; null if none comes
        public async Task<List<DialogMessage>> ReadDialogAsync(TimeSpan? timeout = null)
        {
            var page = new List<DialogMessage>();
            while (true)
            {
                var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTaskDialog, timeout);
                if (packet == null)
                    return page.Count == 0 ? null : page;
                var part = DialogMessage.Parse(packet);
                page.Add(part);
                if (part.Action == DialogMessage.Show)
                    return page;
            }
        }

        public async Task<ItemInfo> WaitForItemAsync(Func<ItemInfo, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItemInfo && match(ItemInfo.Parse(p)), timeout);
            return packet == null ? null : ItemInfo.Parse(packet);
        }

        public Task<PlayerSpawn> WaitForSpawnAsync(uint playerId, TimeSpan? timeout = null) =>
            this.WaitForSpawnAsync(playerId, _ => true, timeout);

        public async Task<PlayerSpawn> WaitForSpawnAsync(
            uint playerId, Func<PlayerSpawn, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPlayer &&
                PlayerSpawn.Parse(p) is var s && s.Id == playerId && match(s), timeout);
            return packet == null ? null : PlayerSpawn.Parse(packet);
        }

        public async Task<bool> WaitForRemovalAsync(uint entityId, TimeSpan? timeout = null) =>
            await this.WaitForActionAsync(ActionMessage.RemoveEntity, entityId, timeout) != null;

        public async Task<ActionMessage> WaitForActionAsync(ushort action, uint entityId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction &&
                ActionMessage.Parse(p) is var a && a.Action == action && a.CharacterId == entityId, timeout);
            return packet == null ? null : ActionMessage.Parse(packet);
        }

        public async Task<WalkMessage> WaitForWalkAsync(uint entityId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgWalk &&
                WalkMessage.Parse(p).CharacterId == entityId, timeout);
            return packet == null ? null : WalkMessage.Parse(packet);
        }

        public Task SayAsync(ushort channel, string sender, string recipient, string text) =>
            this.SendAsync(TalkMessage.Build(channel, sender, recipient, text));

        public async Task<TalkMessage> WaitForTalkAsync(Func<TalkMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTalk && match(TalkMessage.Parse(p)), timeout);
            return packet == null ? null : TalkMessage.Parse(packet);
        }

        public async Task<NameMessage> AskWhisperDetailsAsync(string name, TimeSpan? timeout = null)
        {
            await this.SendAsync(NameMessage.Build(NameMessage.WhisperDetails, name));
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgName && p[8] == NameMessage.WhisperDetails, timeout);
            return packet == null ? null : NameMessage.Parse(packet);
        }

        public async Task<UserAttribMessage> WaitForAttribAsync(
            Func<UserAttribMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgUserAttrib && match(UserAttribMessage.Parse(p)), timeout);
            return packet == null ? null : UserAttribMessage.Parse(packet);
        }

        public Task TradeAsync(uint action, uint data) => this.SendAsync(TradeMessage.Build(action, data));

        public async Task<TradeMessage> WaitForTradeAsync(Func<TradeMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTrade && match(TradeMessage.Parse(p)), timeout);
            return packet == null ? null : TradeMessage.Parse(packet);
        }

        public async Task<RelationMessage> WaitForRelationAsync(Func<RelationMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgRelation && match(RelationMessage.Parse(p)), timeout);
            return packet == null ? null : RelationMessage.Parse(packet);
        }

        public Task AllotAsync(byte strength, byte agility, byte vitality, byte spirit) =>
            this.SendAsync(AllotMessage.Build(strength, agility, vitality, spirit));

        public Task WalkAsync(uint characterId, int direction) =>
            this.SendAsync(WalkMessage.Build(characterId, direction));

        public Task JumpAsync(uint characterId, int fromX, int fromY, int toX, int toY) =>
            this.SendAsync(ActionMessage.BuildJump(characterId, fromX, fromY, toX, toY));

        public Task UsePortalAsync(uint characterId, int x, int y) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Portal, x: x, y: y));

        public Task TurnAsync(uint characterId, int direction) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Turn, direction: direction));

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

namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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

        // What the server listed while entering the map
        public List<FriendMessage> Relationships { get; } = new List<FriendMessage>();
        public List<TradeBuddyMessage> TradePartners { get; } = new List<TradeBuddyMessage>();
        public List<WeaponSkill> Proficiencies { get; } = new List<WeaponSkill>();
        public List<MagicInfo> Spells { get; } = new List<MagicInfo>();

        // What the server sent for the guild step: the guild window, null if none came
        public GuildInfo Guild { get; private set; }

        // The clan window the server sent with the guild step, null if none came
        public FamilyMessage Clan { get; private set; }

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

            // Friends, enemies and trade partners come before the reply to the relationships step
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.LoginRelationships));
            while (true)
            {
                var packet = await this.ReceiveAsync(p =>
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFriend ||
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTradeBuddy && TradeBuddyMessage.Parse(p).Action == TradeBuddyMessage.Add ||
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction &&
                    ActionMessage.Parse(p).Action == ActionMessage.LoginRelationships)
                    ?? throw new InvalidOperationException("Game server closed the connection");
                if (BitConverter.ToUInt16(packet, 2) == PacketTypes.MsgAction)
                    break;
                if (BitConverter.ToUInt16(packet, 2) == PacketTypes.MsgTradeBuddy)
                    this.TradePartners.Add(TradeBuddyMessage.Parse(packet));
                else
                    this.Relationships.Add(FriendMessage.Parse(packet));
            }

            // Proficiencies, then spells, each before the reply to its step
            foreach (var (step, type) in new[]
            {
                (ActionMessage.LoginProficiencies, PacketTypes.MsgWeaponSkill),
                (ActionMessage.LoginSpells, PacketTypes.MsgMagicInfo),
            })
            {
                await this.SendAsync(ActionMessage.Build(characterId, step));
                while (true)
                {
                    var packet = await this.ReceiveAsync(p =>
                        BitConverter.ToUInt16(p, 2) == type ||
                        BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction && ActionMessage.Parse(p).Action == step)
                        ?? throw new InvalidOperationException("Game server closed the connection");
                    if (BitConverter.ToUInt16(packet, 2) == PacketTypes.MsgAction)
                        break;
                    if (type == PacketTypes.MsgWeaponSkill)
                        this.Proficiencies.Add(WeaponSkill.Parse(packet));
                    else
                        this.Spells.Add(MagicInfo.Parse(packet));
                }
            }

            // The guild window and lists come before the reply to the guild step
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.LoginGuild));
            while (true)
            {
                var packet = await this.ReceiveAsync(p =>
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgSyndicateAttributeInfo ||
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFamily && FamilyMessage.Parse(p).Action == FamilyMessage.Query ||
                    BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAction &&
                    ActionMessage.Parse(p).Action == ActionMessage.LoginGuild)
                    ?? throw new InvalidOperationException("Game server closed the connection");
                if (BitConverter.ToUInt16(packet, 2) == PacketTypes.MsgAction)
                    break;
                if (BitConverter.ToUInt16(packet, 2) == PacketTypes.MsgFamily)
                    this.Clan = FamilyMessage.Parse(packet);
                else
                    this.Guild = GuildInfo.Parse(packet);
            }

            await this.SendAsync(ActionMessage.BuildLoginComplete());
            return (spawn, inventory);
        }

        public Task EquipAsync(uint itemId, uint position) =>
            this.SendAsync(ItemMessage.Build(itemId, position, ItemMessage.Equip));

        // Equipping to slot 0 uses the item
        public Task UseAsync(uint itemId) => this.EquipAsync(itemId, 0);

        public Task UnequipAsync(uint itemId, uint position) =>
            this.SendAsync(ItemMessage.Build(itemId, position, ItemMessage.Unequip));

        public async Task<ItemMessage> WaitForItemActionAsync(uint action, uint itemId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItem &&
                ItemMessage.Parse(p) is var m && m.Action == action && m.Id == itemId, timeout);
            return packet == null ? null : ItemMessage.Parse(packet);
        }

        public Task RepairAsync(uint itemId) =>
            this.SendAsync(ItemMessage.Build(itemId, 0, ItemMessage.Repair));

        // The stack in the ID field, how many leave it for a new stack in Command
        public Task SplitAsync(uint itemId, uint amount) =>
            this.SendAsync(ItemMessage.Build(itemId, amount, ItemMessage.Split));

        public Task RepairAllAsync() =>
            this.SendAsync(ItemMessage.Build(0, 0, ItemMessage.RepairAll));

        // The gear in the ID field, the gem in Command; the echo carries the roll in Command
        public Task EnchantAsync(uint itemId, uint gemId) =>
            this.SendAsync(ItemMessage.Build(itemId, gemId, ItemMessage.Enchant));

        public Task ComposeAsync(uint mainId, params uint[] minorIds) =>
            this.SendAsync(ComposeMessage.Build(mainId, minorIds));

        public Task MineAsync(uint characterId, int x, int y) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Mine, x: x, y: y));

        public Task EmbedAsync(uint characterId, uint itemId, uint gemId, ushort socket) =>
            this.SendAsync(GemEmbedMessage.Build(characterId, itemId, gemId, socket, GemEmbedMessage.Embed));

        public Task RemoveGemAsync(uint characterId, uint itemId, ushort socket) =>
            this.SendAsync(GemEmbedMessage.Build(characterId, itemId, 0, socket, GemEmbedMessage.Remove));

        public async Task<GemEmbedMessage> WaitForGemEmbedAsync(Func<GemEmbedMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgGemEmbed && match(GemEmbedMessage.Parse(p)), timeout);
            return packet == null ? null : GemEmbedMessage.Parse(packet);
        }

        public Task DropAsync(uint itemId) =>
            this.SendAsync(ItemMessage.Build(itemId, 0, ItemMessage.Drop));

        public Task DropSilverAsync(uint amount) =>
            this.SendAsync(ItemMessage.Build(amount, 0, ItemMessage.DropSilver));

        public Task BuyAsync(uint npcId, uint typeId, uint amount = 1) =>
            this.SendAsync(ItemMessage.Build(npcId, typeId, ItemMessage.Buy, amount));

        public Task SellAsync(uint npcId, uint itemId) =>
            this.SendAsync(ItemMessage.Build(npcId, itemId, ItemMessage.Sell));

        public Task StoreSilverAsync(uint npcId, uint amount) =>
            this.SendAsync(ItemMessage.Build(npcId, amount, ItemMessage.StoreSilver));

        public Task TakeSilverAsync(uint npcId, uint amount) =>
            this.SendAsync(ItemMessage.Build(npcId, amount, ItemMessage.TakeSilver));

        public async Task<uint?> AskStoredSilverAsync(uint npcId, TimeSpan? timeout = null)
        {
            await this.SendAsync(ItemMessage.Build(npcId, 0, ItemMessage.StoredSilver));
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItem &&
                ItemMessage.Parse(p).Action == ItemMessage.StoredSilver, timeout);
            return packet == null ? null : ItemMessage.Parse(packet).Data;
        }

        public Task SendPackageAsync(uint npcId, byte action, uint itemId = 0) =>
            this.SendAsync(PackageMessage.Build(npcId, action, itemId));

        public async Task<PackageMessage> WaitForPackageAsync(Func<PackageMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPackage && match(PackageMessage.Parse(p)), timeout);
            return packet == null ? null : PackageMessage.Parse(packet);
        }

        // The booth's cell goes in Command; the answer has the booth's ID there. Null if none comes
        public async Task<uint?> OpenBoothAsync(uint characterId, int x, int y, int direction, TimeSpan? timeout = null)
        {
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.CreateBooth,
                command: (uint)(x | (y << 16)), direction: direction, x: x, y: y));
            var answer = await this.WaitForActionAsync(ActionMessage.CreateBooth, characterId, timeout);
            return answer?.Command;
        }

        public Task CloseBoothAsync(uint characterId) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.CloseBooth));

        public Task AddToBoothAsync(uint itemId, uint price, bool conquerPoints = false) =>
            this.SendAsync(ItemMessage.Build(itemId, price, conquerPoints ? ItemMessage.BoothAddCps : ItemMessage.BoothAdd));

        public Task RemoveFromBoothAsync(uint itemId, uint boothId) =>
            this.SendAsync(ItemMessage.Build(itemId, boothId, ItemMessage.BoothRemove));

        public Task ObserveAsync(uint characterId, uint targetId) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Observe, targetId));

        public Task QueryStatsAsync(uint characterId) =>
            this.SendAsync(StatWindow.Build(characterId));

        public async Task<StatWindow> WaitForStatsAsync(Func<StatWindow, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPlayerAttribInfo && match(StatWindow.Parse(p)), timeout);
            return packet == null ? null : StatWindow.Parse(packet);
        }

        public Task ViewBoothAsync(uint boothId) =>
            this.SendAsync(ItemMessage.Build(boothId, 0, ItemMessage.BoothView));

        public Task BuyFromBoothAsync(uint itemId, uint boothId) =>
            this.SendAsync(ItemMessage.Build(itemId, boothId, ItemMessage.BoothBuy));

        public async Task<BoothItem> WaitForBoothItemAsync(Func<BoothItem, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItemInfoEx && match(BoothItem.Parse(p)), timeout);
            return packet == null ? null : BoothItem.Parse(packet);
        }

        public async Task<NpcSpawn> WaitForNpcAsync(Func<NpcSpawn, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgNpcInfo && match(NpcSpawn.Parse(p)), timeout);
            return packet == null ? null : NpcSpawn.Parse(packet);
        }

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

        public async Task<GoalSpawn> WaitForGoalAsync(uint npcId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgNpcInfoEx && GoalSpawn.Parse(p).Id == npcId, timeout);
            return packet == null ? null : GoalSpawn.Parse(packet);
        }

        // A dead mark put on the target, or taken off: remove at 4, target at 8
        public async Task<bool> WaitForDeadMarkAsync(uint target, bool remove, TimeSpan? timeout = null) =>
            await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgDeadMark &&
                BitConverter.ToUInt32(p, 8) == target && (p[4] != 0) == remove, timeout) != null;

        public async Task<AuraMessage> WaitForAuraAsync(Func<AuraMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgAura &&
                match(AuraMessage.Parse(p)), timeout);
            return packet == null ? null : AuraMessage.Parse(packet);
        }

        public Task AskForAuraAsync(uint owner, uint aura) => this.SendAsync(AuraMessage.BuildAsk(owner, aura));

        public Task TalkToNpcAsync(uint npcId) => this.SendAsync(NpcSpawn.BuildClick(npcId));

        // Like the game, which sends 255 after each answer as its dialog window closes
        public async Task AnswerNpcAsync(byte option, string input = null)
        {
            await this.SendAsync(DialogMessage.BuildAnswer(option, input));
            if (option != DialogMessage.NoOption)
                await this.SendAsync(DialogMessage.BuildAnswer(DialogMessage.NoOption));
        }

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

        public async Task<PlayerSpawn> WaitForMonsterAsync(Func<PlayerSpawn, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPlayer &&
                PlayerSpawn.Parse(p) is var s && s.IsMonster && match(s), timeout);
            return packet == null ? null : PlayerSpawn.Parse(packet);
        }

        public Task AttackAsync(uint characterId, uint targetId, int x, int y) =>
            this.SendAsync(InteractMessage.Build(InteractMessage.Attack, characterId, targetId, x, y));

        public Task ShootAsync(uint characterId, uint targetId, int x, int y) =>
            this.SendAsync(InteractMessage.Build(InteractMessage.Shoot, characterId, targetId, x, y));

        public Task CourtAsync(uint characterId, uint targetId, int x, int y) =>
            this.SendAsync(InteractMessage.Build(InteractMessage.Court, characterId, targetId, x, y));

        public Task MarryAsync(uint characterId, uint targetId, int x, int y) =>
            this.SendAsync(InteractMessage.Build(InteractMessage.Marry, characterId, targetId, x, y));

        public Task QuerySpouseAsync(uint targetId) =>
            this.SendAsync(NameMessage.Build(NameMessage.Spouse, string.Empty, targetId));

        public async Task<InteractMessage> WaitForInteractAsync(Func<InteractMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgInteract && match(InteractMessage.Parse(p)), timeout);
            return packet == null ? null : InteractMessage.Parse(packet);
        }

        // A spell effect cast by the caster: the magic type, or null if none came in time
        public async Task<ushort?> WaitForMagicEffectAsync(uint casterId, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgMagicEffect &&
                BitConverter.ToUInt32(p, 4) == casterId, timeout);
            return packet == null ? null : BitConverter.ToUInt16(packet, 12);
        }

        public Task CastAsync(uint characterId, ushort magicType, uint targetId, int x, int y, ushort level = 0) =>
            this.SendAsync(InteractMessage.BuildMagic(characterId, magicType, level, targetId, x, y));

        public Task SwitchCounterKillAsync(uint characterId) =>
            this.SendAsync(InteractMessage.Build(InteractMessage.CounterKillSwitch, characterId, 1, 0, 0));

        public async Task<MagicEffect> WaitForEffectAsync(Func<MagicEffect, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgMagicEffect && match(MagicEffect.Parse(p)), timeout);
            return packet == null ? null : MagicEffect.Parse(packet);
        }

        public async Task<MagicInfo> WaitForMagicInfoAsync(Func<MagicInfo, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgMagicInfo && match(MagicInfo.Parse(p)), timeout);
            return packet == null ? null : MagicInfo.Parse(packet);
        }

        public async Task<WeaponSkill> WaitForWeaponSkillAsync(Func<WeaponSkill, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgWeaponSkill && match(WeaponSkill.Parse(p)), timeout);
            return packet == null ? null : WeaponSkill.Parse(packet);
        }

        // Sent by the client once the death animation is over
        public Task BecomeGhostAsync(uint characterId) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Ghost));

        public Task ReviveAsync(uint characterId) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Revive));

        // The blessed may revive where they died
        public Task ReviveHereAsync(uint characterId) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Revive, command: 1));

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

        public Task SyndicateAsync(uint action, uint data, string text = null) =>
            this.SendAsync(SyndicateMessage.Build(action, data, text));

        public async Task<SyndicateMessage> WaitForSyndicateAsync(
            Func<SyndicateMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgSyndicate && match(SyndicateMessage.Parse(p)), timeout);
            return packet == null ? null : SyndicateMessage.Parse(packet);
        }

        public async Task<GuildInfo> WaitForGuildInfoAsync(Func<GuildInfo, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgSyndicateAttributeInfo && match(GuildInfo.Parse(p)), timeout);
            return packet == null ? null : GuildInfo.Parse(packet);
        }

        public async Task<List<GuildMember>> QueryGuildMembersAsync(uint startIndex = 0, TimeSpan? timeout = null)
        {
            await this.SendAsync(GuildMember.BuildQuery(startIndex));
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgSynMemberList, timeout);
            return packet == null ? null : GuildMember.ParseList(packet);
        }

        public async Task<GuildDonationRanking> QueryGuildDonationRankingAsync(
            byte rankType, byte pageIndex = 0, TimeSpan? timeout = null)
        {
            await this.SendAsync(GuildDonationRanking.BuildQuery(rankType, pageIndex));
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFactionRankInfo && p[4] == rankType, timeout);
            return packet == null ? null : GuildDonationRanking.Parse(packet);
        }

        public Task TotemPoleAsync(ushort action, uint totemType = 0, uint itemId = 0, uint enhancement = 0) =>
            this.SendAsync(TotemPoleMessage.Build(action, totemType, itemId, enhancement));

        public async Task<TotemPoleMessage> WaitForTotemPoleAsync(
            Func<TotemPoleMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTotemPole && match(TotemPoleMessage.Parse(p)), timeout);
            return packet == null ? null : TotemPoleMessage.Parse(packet);
        }

        public async Task<TotemPoleInfoMessage> WaitForTotemPoleInfoAsync(
            Func<TotemPoleInfoMessage, bool> match = null, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTotemPoleInfo &&
                (match == null || match(TotemPoleInfoMessage.Parse(p))), timeout);
            return packet == null ? null : TotemPoleInfoMessage.Parse(packet);
        }

        public async Task<TotemPoleInfoMessage> QueryArsenalAsync(TimeSpan? timeout = null)
        {
            await this.TotemPoleAsync(TotemPoleMessage.Refresh);
            return await this.WaitForTotemPoleInfoAsync(null, timeout);
        }

        public async Task<WeaponsInfoMessage> QueryArsenalWeaponsAsync(
            uint totemType, uint beginAt = 1, uint endAt = 8, TimeSpan? timeout = null)
        {
            await this.SendAsync(WeaponsInfoMessage.BuildQuery(totemType, beginAt, endAt));
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgWeaponsInfo &&
                BitConverter.ToUInt32(p, 16) == totemType, timeout);
            return packet == null ? null : WeaponsInfoMessage.Parse(packet);
        }

        public Task FamilyAsync(uint action, uint data, string text = null) =>
            this.SendAsync(FamilyMessage.Build(action, data, text));

        public async Task<FamilyMessage> WaitForFamilyAsync(Func<FamilyMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFamily && match(FamilyMessage.Parse(p)), timeout);
            return packet == null ? null : FamilyMessage.Parse(packet);
        }

        public Task FamilyOccupyAsync(uint action, uint npcId) =>
            this.SendAsync(FamilyOccupyMessage.Build(action, npcId));

        public async Task<FamilyOccupyMessage> WaitForFamilyOccupyAsync(Func<FamilyOccupyMessage, bool> match,
            TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFamilyOccupy && match(FamilyOccupyMessage.Parse(p)), timeout);
            return packet == null ? null : FamilyOccupyMessage.Parse(packet);
        }

        public Task NameAsync(byte action, string text, uint data = 0) =>
            this.SendAsync(NameMessage.Build(action, text, data));

        public async Task<NameMessage> WaitForNameAsync(Func<NameMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgName && match(NameMessage.Parse(p)), timeout);
            return packet == null ? null : NameMessage.Parse(packet);
        }

        public Task TeamAsync(uint action, uint data) => this.SendAsync(TeamMessage.Build(action, data));

        public async Task<TeamMessage> WaitForTeamAsync(Func<TeamMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTeam && match(TeamMessage.Parse(p)), timeout);
            return packet == null ? null : TeamMessage.Parse(packet);
        }

        public async Task<TeamMemberMessage> WaitForTeamMemberAsync(
            Func<TeamMemberMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTeamMember && match(TeamMemberMessage.Parse(p)), timeout);
            return packet == null ? null : TeamMemberMessage.Parse(packet);
        }

        public Task FriendAsync(byte action, uint id, string name = "") =>
            this.SendAsync(FriendMessage.Build(action, id, name));

        public async Task<FriendMessage> WaitForFriendAsync(Func<FriendMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFriend && match(FriendMessage.Parse(p)), timeout);
            return packet == null ? null : FriendMessage.Parse(packet);
        }

        public Task RequestEnemyListAsync(uint userId) => this.SendAsync(EnemyListMessage.Build(userId));

        public async Task<EnemyListMessage> WaitForEnemyListAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgEnemyList, timeout);
            return packet == null ? null : EnemyListMessage.Parse(packet);
        }

        public Task PigeonAsync(byte action, uint param = 0, string text = null, byte currency = 1) =>
            this.SendAsync(PigeonMessage.Build(action, param, text, currency));

        public async Task<PigeonMessage> WaitForPigeonAsync(Func<PigeonMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPigeon && match(PigeonMessage.Parse(p)), timeout);
            return packet == null ? null : PigeonMessage.Parse(packet);
        }

        public async Task<PigeonQueryMessage> WaitForPigeonQueryAsync(
            Func<PigeonQueryMessage, bool> match = null, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPigeonQuery &&
                (match == null || match(PigeonQueryMessage.Parse(p))), timeout);
            return packet == null ? null : PigeonQueryMessage.Parse(packet);
        }

        public Task MessageBoardAsync(ushort index, ushort channel, byte action, string name = null) =>
            this.SendAsync(MessageBoardMessage.Build(index, channel, action, name));

        public async Task<MessageBoardMessage> WaitForMessageBoardAsync(
            Func<MessageBoardMessage, bool> match = null, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgMessageBoard &&
                (match == null || match(MessageBoardMessage.Parse(p))), timeout);
            return packet == null ? null : MessageBoardMessage.Parse(packet);
        }

        public Task LockAsync(uint itemId) => this.SendAsync(EquipLockMessage.Build(itemId, EquipLockMessage.Lock));

        public Task UnlockAsync(uint itemId) => this.SendAsync(EquipLockMessage.Build(itemId, EquipLockMessage.Unlock));

        public async Task<EquipLockMessage> WaitForEquipLockAsync(Func<EquipLockMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgEquipLock && match(EquipLockMessage.Parse(p)), timeout);
            return packet == null ? null : EquipLockMessage.Parse(packet);
        }

        public Task PasswordAsync(uint request, uint password, uint newPassword = 0) =>
            this.SendAsync(PasswordMessage.Build(request, password, newPassword));

        public async Task<PasswordMessage> WaitForPasswordAsync(Func<PasswordMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.Msg2ndPsw && match(PasswordMessage.Parse(p)), timeout);
            return packet == null ? null : PasswordMessage.Parse(packet);
        }

        public Task TradeBuddyAsync(byte action, uint id) => this.SendAsync(TradeBuddyMessage.Build(action, id));

        public async Task<TradeBuddyMessage> WaitForTradeBuddyAsync(Func<TradeBuddyMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTradeBuddy && match(TradeBuddyMessage.Parse(p)), timeout);
            return packet == null ? null : TradeBuddyMessage.Parse(packet);
        }

        // Answered only while the partner is online
        public async Task<TradeBuddyInfo> AskPartnerInfoAsync(uint characterId, uint partnerId, TimeSpan? timeout = null)
        {
            await this.SendAsync(ActionMessage.Build(characterId, ActionMessage.PartnerInfo, partnerId));
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTradeBuddyInfo, timeout);
            return packet == null ? null : TradeBuddyInfo.Parse(packet);
        }

        // The weather code, intensity and direction
        public async Task<(uint Code, uint Intensity, int Direction)?> WaitForWeatherAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgWeather, timeout);
            return packet == null ? null
                : (BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8), BitConverter.ToInt32(packet, 12));
        }

        public Task GodExpAsync(byte action) => this.SendAsync(GodExpMessage.Build(action));

        public async Task<GodExpMessage> WaitForGodExpAsync(Func<GodExpMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgGodExp && match(GodExpMessage.Parse(p)), timeout);
            return packet == null ? null : GodExpMessage.Parse(packet);
        }

        public Task TrainingAsync(ushort action) => this.SendAsync(TrainingMessage.Build(action));

        public async Task<TrainingMessage> WaitForTrainingAsync(ushort action, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTraining && TrainingMessage.Parse(p).Action == action, timeout);
            return packet == null ? null : TrainingMessage.Parse(packet);
        }

        public async Task<TrainingInfo> WaitForTrainingInfoAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgTrainingInfo, timeout);
            return packet == null ? null : TrainingInfo.Parse(packet);
        }

        public Task PeerageAsync(ushort action, ulong data, ushort currency = 0) =>
            this.SendAsync(PeerageMessage.Build(action, data, currency));

        public async Task<byte[]> WaitForPeerageAsync(Func<PeerageMessage, bool> match, TimeSpan? timeout = null) =>
            await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgPeerage && match(PeerageMessage.Parse(p)), timeout);

        public async Task<QualifierMessage> WaitForQualifierAsync(Func<QualifierMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgQualifyingInteractive && match(QualifierMessage.Parse(p)), timeout);
            return packet == null ? null : QualifierMessage.Parse(packet);
        }

        public async Task<QualifierDetails> WaitForQualifierDetailsAsync(Func<QualifierDetails, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgQualifyingDetailInfo && match(QualifierDetails.Parse(p)), timeout);
            return packet == null ? null : QualifierDetails.Parse(packet);
        }

        public async Task<QualifierMatches> WaitForQualifierMatchesAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgQualifyingFightersList, timeout);
            return packet == null ? null : QualifierMatches.Parse(packet);
        }

        public async Task<QualifierRanking> WaitForQualifierRankingAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgQualifyingRank, timeout);
            return packet == null ? null : QualifierRanking.Parse(packet);
        }

        public async Task<QualifierSeasonRanking> WaitForQualifierSeasonRankingAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgQualifyingSeasonRankList, timeout);
            return packet == null ? null : QualifierSeasonRanking.Parse(packet);
        }

        public async Task<ArenaScore> WaitForArenaScoreAsync(Func<ArenaScore, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgArenicScore && match(ArenaScore.Parse(p)), timeout);
            return packet == null ? null : ArenaScore.Parse(packet);
        }

        public Task AllotAsync(byte strength, byte agility, byte vitality, byte spirit) =>
            this.SendAsync(AllotMessage.Build(strength, agility, vitality, spirit));

        public Task WalkAsync(uint characterId, int direction) =>
            this.SendAsync(WalkMessage.Build(characterId, direction));

        public Task RideAsync(uint characterId, int direction) =>
            this.SendAsync(WalkMessage.Build(characterId, direction, WalkMessage.Ride));

        public async Task<uint?> WaitForVigorAsync(Func<uint, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgData && DataMessage.Parse(p) is var d &&
                d.Action == DataMessage.Vigor && match(d.Value), timeout);
            return packet == null ? null : DataMessage.Parse(packet).Value;
        }

        public Task JumpAsync(uint characterId, int fromX, int fromY, int toX, int toY) =>
            this.SendAsync(ActionMessage.BuildJump(characterId, fromX, fromY, toX, toY));

        public Task UsePortalAsync(uint characterId, int x, int y) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Portal, x: x, y: y));

        public Task TurnAsync(uint characterId, int direction) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Turn, direction: direction));

        public Task EmoteAsync(uint characterId, uint stance, int direction) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.Emote, command: stance, direction: direction));

        public Task QueryDetainedAsync(uint characterId, uint page) =>
            this.SendAsync(ActionMessage.Build(characterId, ActionMessage.DetainQuery, command: page));

        public Task RedeemDetainedAsync(uint detainId) =>
            this.SendAsync(ItemMessage.Build(detainId, 0, ItemMessage.RedeemEquipment));

        public Task ClaimDetainedAsync(uint detainId) =>
            this.SendAsync(ItemMessage.Build(detainId, 0, ItemMessage.ClaimDetained));

        public async Task<DetainItemInfoMessage> WaitForDetainItemInfoAsync(
            Func<DetainItemInfoMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgDetainItemInfo && match(DetainItemInfoMessage.Parse(p)), timeout);
            return packet == null ? null : DetainItemInfoMessage.Parse(packet);
        }

        public async Task<DetainItemUpdateMessage> WaitForDetainItemUpdateAsync(
            Func<DetainItemUpdateMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgDetainItemUpdate && match(DetainItemUpdateMessage.Parse(p)), timeout);
            return packet == null ? null : DetainItemUpdateMessage.Parse(packet);
        }

        public Task ReincarnateAsync(int profession, int body) =>
            this.SendAsync(MeteSpecialMessage.Build(profession, body));

        public async Task<MeteSpecialMessage> WaitForReincarnationAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgMeteSpecial, timeout);
            return packet == null ? null : MeteSpecialMessage.Parse(packet);
        }

        public Task QueryNameChangeAsync() =>
            this.SendAsync(ChangeNameMessage.Build(ChangeNameMessage.Query));

        public Task ChangeNameAsync(string name) =>
            this.SendAsync(ChangeNameMessage.Build(ChangeNameMessage.Change, name));

        public async Task<ChangeNameMessage> WaitForNameChangeAsync(TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgChangeName, timeout);
            return packet == null ? null : ChangeNameMessage.Parse(packet);
        }

        public Task RefineAsync(uint targetId, uint refineryItemId) =>
            this.SendAsync(QuenchMessage.Build(targetId, refineryItemId));

        public Task StabilizeAsync(uint equipmentId, params uint[] stoneIds) =>
            this.SendAsync(SolidifyMessage.Build(equipmentId, stoneIds));

        public async Task<ItemStatusEntry> WaitForItemStatusAsync(Func<ItemStatusEntry, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgItemStatus &&
                ItemStatusMessage.Parse(p).Entries.Any(match), timeout);
            return packet == null ? null : ItemStatusMessage.Parse(packet).Entries.First(match);
        }

        public Task DegradeItemAsync(uint itemId) =>
            this.SendAsync(ItemMessage.Build(itemId, 0, 54));

        public Task GuideAsync(uint action, uint identity, uint param, uint value = 1) =>
            this.SendAsync(GuideMessage.Build(action, identity, param, value));

        public async Task<GuideMessage> WaitForGuideAsync(Func<GuideMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgGuide && match(GuideMessage.Parse(p)), timeout);
            return packet == null ? null : GuideMessage.Parse(packet);
        }

        public Task QueryGuideInfoAsync(uint mode, uint identity = 0) =>
            this.SendAsync(GuideInfoMessage.BuildQuery(mode, identity));

        public async Task<GuideInfoMessage> WaitForGuideInfoAsync(Func<GuideInfoMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgGuideInfo && match(GuideInfoMessage.Parse(p)), timeout);
            return packet == null ? null : GuideInfoMessage.Parse(packet);
        }

        public Task ContributeAsync(uint action, ulong experience = 0) =>
            this.SendAsync(ContributeMessage.Build(action, experience));

        public async Task<ContributeMessage> WaitForContributeAsync(Func<ContributeMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgContribute && match(ContributeMessage.Parse(p)), timeout);
            return packet == null ? null : ContributeMessage.Parse(packet);
        }

        public Task SendSubProAsync(ushort action, byte subClass = 0) =>
            this.SendAsync(SubProMessage.Build(action, subClass));

        public async Task<SubProMessage> WaitForSubProAsync(Func<SubProMessage, bool> match, TimeSpan? timeout = null)
        {
            var packet = await this.TryReceiveAsync(p =>
                BitConverter.ToUInt16(p, 2) == PacketTypes.MsgSubPro && match(SubProMessage.Parse(p)), timeout);
            return packet == null ? null : SubProMessage.Parse(packet);
        }

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

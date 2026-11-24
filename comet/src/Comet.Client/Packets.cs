namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public static class PacketTypes
    {
        public const ushort MsgRegister = 1001;
        public const ushort MsgTalk = 1004;
        public const ushort MsgUserInfo = 1006;
        public const ushort MsgItemInfo = 1008;
        public const ushort MsgItem = 1009;
        public const ushort MsgName = 1015;
        public const ushort MsgFriend = 1019;
        public const ushort MsgTeam = 1023;
        public const ushort MsgTeamMember = 1026;
        public const ushort MsgData = 1033;
        public const ushort MsgGemEmbed = 1027;
        public const ushort MsgTrade = 1056;
        public const ushort MsgMapItem = 1101;
        public const ushort MsgItemInfoEx = 1108;
        public const ushort MsgRelation = 2071;
        public const ushort MsgDeadMark = 2081;
        public const ushort MsgAura = 2410;
        public const ushort MsgPeerage = 2064;
        public const ushort MsgQualifyingInteractive = 2205;
        public const ushort MsgQualifyingFightersList = 2206;
        public const ushort MsgQualifyingRank = 2207;
        public const ushort MsgQualifyingSeasonRankList = 2208;
        public const ushort MsgQualifyingDetailInfo = 2209;
        public const ushort MsgArenicScore = 2210;
        public const ushort MsgSynpOffer = 1058;
        public const ushort MsgSyndicateAttributeInfo = 1106;
        public const ushort MsgSyndicate = 1107;
        public const ushort MsgSynMemberList = 2102;
        public const ushort MsgFamily = 1312;
        public const ushort MsgPackage = 1102;
        public const ushort MsgInteract = 1022;
        public const ushort MsgMagicEffect = 1105;
        public const ushort MsgMagicInfo = 1103;
        public const ushort MsgWeaponSkill = 1025;
        public const ushort MsgNpcInfo = 2030;
        public const ushort MsgNpcInfoEx = 1109;
        public const ushort MsgNpc = 2031;
        public const ushort MsgTaskDialog = 2032;
        public const ushort MsgAllot = 1024;
        public const ushort MsgConnect = 1052;
        public const ushort MsgConnectEx = 1055;
        public const ushort MsgEncryptCode = 1059;
        public const ushort MsgAccountSRP6 = 1124;
        public const ushort MsgLoginChallengeS = 1213;
        public const ushort MsgLoginProofC = 1214;
        public const ushort MsgAction = 10010;
        public const ushort MsgWalk = 10005;
        public const ushort MsgPlayer = 10014;
        public const ushort MsgUserAttrib = 10017;
        public const ushort MsgGodExp = 1036;
        public const ushort MsgPlayerAttribInfo = 1040;
        public const ushort MsgTrainingInfo = 2043;
        public const ushort MsgTraining = 2044;
        public const ushort MsgTradeBuddy = 2046;
        public const ushort MsgTradeBuddyInfo = 2047;
        public const ushort MsgEquipLock = 2048;
        public const ushort Msg2ndPsw = 2261;
    }

    public static class TalkChannels
    {
        public const ushort Talk = 2000;
        public const ushort Whisper = 2001;
        public const ushort Team = 2003;
        public const ushort Guild = 2004;
        public const ushort Clan = 2006;
        public const ushort Friend = 2009;
        public const ushort Center = 2011;
        public const ushort Ghost = 2013;
        public const ushort Service = 2014;
        public const ushort World = 2021;
        public const ushort Vendor = 2104;
        public const ushort Register = 2100;
        public const ushort Login = 2101;
    }

    public sealed record TalkMessage(ushort Channel, string Sender, string Recipient, string Text)
    {
        public static TalkMessage Parse(byte[] packet)
        {
            var strings = Packet.ReadStrings(packet, 24);
            string At(int i) => i < strings.Count ? strings[i] : "";
            return new TalkMessage(BitConverter.ToUInt16(packet, 8), At(0), At(1), At(3));
        }

        // Color, channel, style, an ID, the recipient's and sender's mesh, then the strings
        // sender, recipient, suffix and text
        public static byte[] Build(ushort channel, string sender, string recipient, string text)
        {
            var strings = new[] { sender, recipient, "", text }
                .Select(s => Encoding.Latin1.GetBytes(s)).ToArray();
            var packet = Packet.Create(PacketTypes.MsgTalk, 25 + strings.Sum(s => 1 + s.Length));
            BitConverter.TryWriteBytes(packet.AsSpan(4), 0x00FFFFFF);
            BitConverter.TryWriteBytes(packet.AsSpan(8), channel);
            int offset = 24;
            packet[offset++] = (byte)strings.Length;
            foreach (var s in strings)
            {
                packet[offset++] = (byte)s.Length;
                s.CopyTo(packet, offset);
                offset += s.Length;
            }
            return packet;
        }
    }

    // A number, the action as one byte, then the strings
    public sealed record NameMessage(uint Data, byte Action, List<string> Strings)
    {
        public const byte Guild = 3, SetSpouse = 6, RoleEffect = 10, GuildKick = 12, Spouse = 16, SetAlly = 21, SetEnemy = 22, WhisperDetails = 26;

        public static NameMessage Parse(byte[] packet) => new NameMessage(
            BitConverter.ToUInt32(packet, 4), packet[8], Packet.ReadStrings(packet, 9));

        public static byte[] Build(byte action, string text, uint data = 0)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            var packet = Packet.Create(PacketTypes.MsgName, 11 + bytes.Length);
            BitConverter.TryWriteBytes(packet.AsSpan(4), data);
            packet[8] = action;
            packet[9] = 1;
            packet[10] = (byte)bytes.Length;
            bytes.CopyTo(packet, 11);
            return packet;
        }
    }

    public sealed record UserInfo(
        uint Id, uint Mesh, ushort Hairstyle, uint Silver, ushort Strength, ushort Agility,
        ushort Vitality, ushort Spirit, ushort HealthPoints, byte Level, byte Class, string Name,
        ulong Experience, ushort AttributePoints, ushort ManaPoints)
    {
        public ushort PkPoints { get; init; }
        public byte Nobility { get; init; }
        public string Spouse { get; init; }

        public static UserInfo Parse(byte[] packet)
        {
            var strings = Packet.ReadStrings(packet, 110);
            return new UserInfo(
                BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 10),
                BitConverter.ToUInt16(packet, 14), BitConverter.ToUInt32(packet, 16),
                BitConverter.ToUInt16(packet, 52), BitConverter.ToUInt16(packet, 54),
                BitConverter.ToUInt16(packet, 56), BitConverter.ToUInt16(packet, 58),
                BitConverter.ToUInt16(packet, 62), packet[68], packet[69],
                strings[0], BitConverter.ToUInt64(packet, 24),
                BitConverter.ToUInt16(packet, 60), BitConverter.ToUInt16(packet, 64))
            {
                PkPoints = BitConverter.ToUInt16(packet, 66),
                Nobility = packet[72],
                Spouse = strings.Count > 2 ? strings[2] : string.Empty,
            };
        }
    }

    // Monsters come as players too, with their health at 84 and level at 90
    public sealed record PlayerSpawn(
        uint Id, uint Mesh, ushort X, ushort Y, ushort Hairstyle, byte Direction,
        uint Stance, ushort Level, ushort Class, string Name,
        uint Helmet, uint Armor, uint RightHand, uint LeftHand,
        ulong Status = 0, uint MonsterHealth = 0, ushort MonsterLevel = 0)
    {
        public uint GuildId { get; init; }
        public uint GuildRank { get; init; }
        public uint ClanId { get; init; }
        public uint ClanRank { get; init; }
        public string ClanName { get; init; }
        public bool Away { get; init; }
        public uint Steed { get; init; }
        public byte SteedPlus { get; init; }
        public uint Nobility { get; init; }
        // Status bits 64-127, at 30
        public ulong StatusHigh { get; init; }

        public const uint FirstMonsterId = 400001, LastMonsterId = 499999;

        public bool IsMonster => this.Id >= FirstMonsterId && this.Id <= LastMonsterId;

        public static PlayerSpawn Parse(byte[] packet) => new PlayerSpawn(
            BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 4),
            BitConverter.ToUInt16(packet, 92), BitConverter.ToUInt16(packet, 94),
            BitConverter.ToUInt16(packet, 96), packet[98], BitConverter.ToUInt32(packet, 99),
            BitConverter.ToUInt16(packet, 107), BitConverter.ToUInt16(packet, 219),
            Packet.ReadStrings(packet, 226)[0],
            BitConverter.ToUInt32(packet, 44), BitConverter.ToUInt32(packet, 52),
            BitConverter.ToUInt32(packet, 56), BitConverter.ToUInt32(packet, 60),
            BitConverter.ToUInt64(packet, 22), BitConverter.ToUInt32(packet, 84),
            BitConverter.ToUInt16(packet, 90))
        {
            GuildId = BitConverter.ToUInt32(packet, 12),
            GuildRank = BitConverter.ToUInt32(packet, 16),
            ClanId = BitConverter.ToUInt32(packet, 163),
            ClanRank = BitConverter.ToUInt32(packet, 167),
            ClanName = Packet.ReadStrings(packet, 226) is var s && s.Count > 2 ? s[2] : "",
            Away = packet[110] != 0,
            Steed = BitConverter.ToUInt32(packet, 72),
            SteedPlus = packet[141],
            Nobility = BitConverter.ToUInt32(packet, 127),
            StatusHigh = BitConverter.ToUInt64(packet, 30),
        };
    }

    // A timestamp, sender and target, the cell, the action at 20 and its data (the damage, or
    // for a kill the count) at 24; 40 bytes
    public sealed record InteractMessage(uint Sender, uint Target, ushort X, ushort Y, uint Action, uint Data)
    {
        public const uint Attack = 2, Court = 8, Marry = 9, Kill = 14, MagicAttack = 24, Shoot = 28, CounterKill = 43, CounterKillSwitch = 44, Dash = 27;

        // A spell cast: the magic type in the low half of data and the level in the high half;
        // type, target, cell and level are hidden with the sender's ID
        public static byte[] BuildMagic(uint sender, ushort magicType, ushort level, uint target, int x, int y)
        {
            uint timestamp = (uint)Environment.TickCount;
            uint type = (ExchangeShortBits(magicType - 0x14BEu, 3) ^ sender ^ 0x915D) & 0xFFFF;
            uint encodedLevel = ((level + 0x100 * (timestamp % 0x100)) ^ 0x3721) & 0xFFFF;
            var packet = Build(MagicAttack, sender,
                ExchangeLongBits((target - 0x8B90B51A) ^ sender ^ 0x5F2D2463, 32 - 13),
                (int)((ExchangeShortBits((uint)x - 0xDD12, 1) ^ sender ^ 0x2ED6) & 0xFFFF),
                (int)((ExchangeShortBits((uint)y - 0x76DE, 5) ^ sender ^ 0xB99B) & 0xFFFF));
            BitConverter.TryWriteBytes(packet.AsSpan(4), timestamp);
            BitConverter.TryWriteBytes(packet.AsSpan(24), type | (encodedLevel << 16));
            return packet;
        }

        private static uint ExchangeShortBits(uint data, int bits)
        {
            data &= 0xFFFF;
            return ((data >> bits) | (data << (16 - bits))) & 0xFFFF;
        }

        private static uint ExchangeLongBits(uint data, int bits) => (data >> bits) | (data << (32 - bits));

        public static InteractMessage Parse(byte[] packet) => new InteractMessage(
            BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 12),
            BitConverter.ToUInt16(packet, 16), BitConverter.ToUInt16(packet, 18),
            BitConverter.ToUInt32(packet, 20), BitConverter.ToUInt32(packet, 24));

        public static byte[] Build(uint action, uint sender, uint target, int x, int y)
        {
            var packet = Packet.Create(PacketTypes.MsgInteract, 40);
            BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)Environment.TickCount);
            BitConverter.TryWriteBytes(packet.AsSpan(8), sender);
            BitConverter.TryWriteBytes(packet.AsSpan(12), target);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (ushort)x);
            BitConverter.TryWriteBytes(packet.AsSpan(18), (ushort)y);
            BitConverter.TryWriteBytes(packet.AsSpan(20), action);
            return packet;
        }
    }

    // A learned spell: experience at 4, magic type at 8 and level at 10, both ushorts
    public sealed record MagicInfo(ushort Type, ushort Level, uint Experience)
    {
        public static MagicInfo Parse(byte[] packet) => new MagicInfo(
            BitConverter.ToUInt16(packet, 8), BitConverter.ToUInt16(packet, 10), BitConverter.ToUInt32(packet, 4));
    }

    // A weapon proficiency: weapon type (item type / 1000), level, experience and what the
    // level needs, from 4
    public sealed record WeaponSkill(uint Type, uint Level, uint Experience, uint Needed)
    {
        public static WeaponSkill Parse(byte[] packet) => new WeaponSkill(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16));
    }

    // A spell's effect: caster, cell, magic type and level, then per target 32 bytes from 20,
    // starting with its ID and damage
    public sealed record MagicEffect(uint Caster, ushort X, ushort Y, ushort Type, ushort Level,
        List<(uint Id, uint Damage)> Targets)
    {
        public static MagicEffect Parse(byte[] packet)
        {
            var targets = new List<(uint, uint)>();
            uint count = BitConverter.ToUInt32(packet, 16);
            for (int i = 0, offset = 20; i < count && offset + 8 <= packet.Length; i++, offset += 32)
                targets.Add((BitConverter.ToUInt32(packet, offset), BitConverter.ToUInt32(packet, offset + 4)));
            return new MagicEffect(BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt16(packet, 8),
                BitConverter.ToUInt16(packet, 10), BitConverter.ToUInt16(packet, 12),
                BitConverter.ToUInt16(packet, 14), targets);
        }
    }

    public sealed record ActionMessage(
        uint CharacterId, uint Command, ushort Action, ushort Direction, ushort X, ushort Y)
    {
        // Arguments at 12 and 14 and the timestamp field at 16; windows and shops use them
        public ushort Argument1 { get; init; }
        public ushort Argument2 { get; init; }
        public uint Timestamp { get; init; }

        public const ushort LoginSpawn = 74;
        public const ushort LoginInventory = 75;
        public const ushort LoginRelationships = 76;
        public const ushort LoginProficiencies = 77;
        public const ushort LoginSpells = 78;
        public const ushort LoginGuild = 97;
        public const ushort Turn = 79;
        public const ushort Emote = 81;
        public const ushort LevelUp = 92;
        public const ushort SpellRemove = 109;
        public const ushort Revive = 94;
        public const ushort Ghost = 145;
        public const ushort PkMode = 96;
        public const ushort Portal = 85;
        public const ushort Teleport = 86;
        public const ushort RemoveEntity = 135;
        public const ushort Jump = 137;
        public const ushort LoginComplete = 251;
        public const ushort QueryStatInfo = 408;
        public const ushort OpenWindow = 126, OpenShop = 160;
        public const ushort CharacterAway = 161;
        public const ushort ClientCommand = 116, Observe = 117;
        public const ushort PartnerInfo = 152;
        public const ushort ChangeAvatar = 151;
        public const ushort CreateBooth = 111, CloseBooth = 114;
        public const uint WarehouseWindow = 4, ComposeWindow = 2, EnchantWindow = 1091;
        public const ushort Mine = 99;

        public static ActionMessage Parse(byte[] packet) => new ActionMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 20), BitConverter.ToUInt16(packet, 22),
            BitConverter.ToUInt16(packet, 24), BitConverter.ToUInt16(packet, 26))
        {
            Argument1 = BitConverter.ToUInt16(packet, 12),
            Argument2 = BitConverter.ToUInt16(packet, 14),
            Timestamp = BitConverter.ToUInt32(packet, 16),
        };

        public static byte[] Build(uint characterId, ushort action, uint command = 0,
            int direction = 0, int x = 0, int y = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(packet.AsSpan(4), characterId);
            BitConverter.TryWriteBytes(packet.AsSpan(8), command);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            BitConverter.TryWriteBytes(packet.AsSpan(20), action);
            BitConverter.TryWriteBytes(packet.AsSpan(22), (ushort)direction);
            BitConverter.TryWriteBytes(packet.AsSpan(24), (ushort)x);
            BitConverter.TryWriteBytes(packet.AsSpan(26), (ushort)y);
            return packet;
        }

        public static byte[] BuildJump(uint characterId, int fromX, int fromY, int toX, int toY)
        {
            var packet = Build(characterId, Jump, (uint)(toX | (toY << 16)), x: fromX, y: fromY);
            BitConverter.TryWriteBytes(packet.AsSpan(32), -1);
            return packet;
        }

        public static byte[] BuildLoginComplete()
        {
            var packet = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(packet.AsSpan(16), 1u);
            BitConverter.TryWriteBytes(packet.AsSpan(20), LoginComplete);
            return packet;
        }
    }

    // An identity, a count, then per attribute a type (uint) and value (ulong) in 24 bytes;
    // status bits 64-127 follow the value
    public sealed record UserAttribMessage(uint Id, Dictionary<uint, ulong> Values)
    {
        public ulong StatusHigh { get; init; }

        public const uint Health = 0, MaxHealth = 1, Mana = 2, MaxMana = 3, Silver = 4,
            Experience = 5, Class = 7, AttributePoints = 10, Level = 12, Spirit = 13, Vitality = 14,
            Strength = 15, Agility = 16, HeavenBlessing = 17, Mesh = 11, PkPoints = 6, Status = 25, Stamina = 8, XpCircle = 27, Rebirths = 22, Jewels = 29;
        public const ulong DeadStatus = 0x400;

        public const ulong Flashing = 0x1, RedName = 0x4000, BlackName = 0x8000;
        public const ulong XpFull = 0x10, Superman = 0x40000, Cyclone = 0x800000;
        public const ulong Blessed = 1UL << 33;

        public static UserAttribMessage Parse(byte[] packet)
        {
            var values = new Dictionary<uint, ulong>();
            ulong high = 0;
            uint count = BitConverter.ToUInt32(packet, 8);
            for (int i = 0, offset = 12; i < count && offset + 20 <= packet.Length; i++, offset += 24)
            {
                uint type = BitConverter.ToUInt32(packet, offset);
                values[type] = BitConverter.ToUInt64(packet, offset + 4);
                if (type == Status)
                    high = BitConverter.ToUInt64(packet, offset + 12);
            }
            return new UserAttribMessage(BitConverter.ToUInt32(packet, 4), values) { StatusHigh = high };
        }
    }

    // Item ID, type, amount and its limit, then the action and position as ushorts
    // The sockets are the bytes at 24 and 25, the plus at 33, the enchantment at 36, Locked the
    // byte at 46 and the plus progress the uint at 52
    public sealed record ItemInfo(uint Id, uint TypeId, ushort Amount, ushort AmountLimit, ushort Action, ushort Position)
    {
        public bool Locked { get; init; }
        public byte Gem1 { get; init; }
        public byte Gem2 { get; init; }
        public byte Plus { get; init; }
        public byte Enchant { get; init; }
        public uint PlusProgress { get; init; }

        public static ItemInfo Parse(byte[] packet) => new ItemInfo(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 12), BitConverter.ToUInt16(packet, 14),
            BitConverter.ToUInt16(packet, 16), BitConverter.ToUInt16(packet, 18))
        {
            Locked = packet.Length > 46 && packet[46] != 0,
            Gem1 = packet.Length > 25 ? packet[24] : (byte)0,
            Gem2 = packet.Length > 25 ? packet[25] : (byte)0,
            Plus = packet.Length > 36 ? packet[33] : (byte)0,
            Enchant = packet.Length > 36 ? packet[36] : (byte)0,
            PlusProgress = packet.Length >= 56 ? BitConverter.ToUInt32(packet, 52) : 0,
        };
    }

    // The compose window's MsgFuse: the main item at 4, 0 at 8, the count of minors at 10,
    // then the minors from 12
    public static class ComposeMessage
    {
        public const ushort Type = 1028;

        public static byte[] Build(uint mainId, params uint[] minorIds)
        {
            var packet = Packet.Create(Type, 12 + 4 * minorIds.Length);
            BitConverter.TryWriteBytes(packet.AsSpan(4), mainId);
            BitConverter.TryWriteBytes(packet.AsSpan(10), (ushort)minorIds.Length);
            for (int i = 0; i < minorIds.Length; i++)
                BitConverter.TryWriteBytes(packet.AsSpan(12 + 4 * i), minorIds[i]);
            return packet;
        }
    }

    // Gems in and out of sockets, 20 bytes: player ID at 4, item at 8, gem at 12, then the
    // socket (1 or 2) and the action (0 embed, 1 remove) as ushorts
    public sealed record GemEmbedMessage(uint CharacterId, uint ItemId, uint GemId, ushort Socket, ushort Action)
    {
        public const ushort Embed = 0, Remove = 1;

        public static byte[] Build(uint characterId, uint itemId, uint gemId, ushort socket, ushort action)
        {
            var packet = Packet.Create(PacketTypes.MsgGemEmbed, 20);
            BitConverter.TryWriteBytes(packet.AsSpan(4), characterId);
            BitConverter.TryWriteBytes(packet.AsSpan(8), itemId);
            BitConverter.TryWriteBytes(packet.AsSpan(12), gemId);
            BitConverter.TryWriteBytes(packet.AsSpan(16), socket);
            BitConverter.TryWriteBytes(packet.AsSpan(18), action);
            return packet;
        }

        public static GemEmbedMessage Parse(byte[] packet) => new GemEmbedMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 12),
            BitConverter.ToUInt16(packet, 16), BitConverter.ToUInt16(packet, 18));
    }

    // Item locks, 16 bytes: item ID at 4, mode byte at 8, a byte at 9, a value at 12 (the
    // unlock date as yyyymmdd, with 3 at 9)
    public sealed record EquipLockMessage(uint ItemId, byte Mode, byte Flag, uint Value)
    {
        public const byte Lock = 0, Unlock = 1, Unlocked = 3;

        public static EquipLockMessage Parse(byte[] packet) => new EquipLockMessage(
            BitConverter.ToUInt32(packet, 4), packet[8], packet[9], BitConverter.ToUInt32(packet, 12));

        public static byte[] Build(uint itemId, byte mode)
        {
            var packet = Packet.Create(PacketTypes.MsgEquipLock, 16);
            BitConverter.TryWriteBytes(packet.AsSpan(4), itemId);
            packet[8] = mode;
            return packet;
        }
    }

    // The second password, 16 bytes: request at 4, password at 8, new password at 12. The
    // server's Correct carries flags at 8: 0x1 one is set, 0x100 it was given.
    public sealed record PasswordMessage(uint Request, uint Password, uint NewPassword)
    {
        public const uint Verify = 1, Forgot = 2, Set = 3, Correct = 5, Wrong = 6;
        public const uint HasPassword = 0x1, Verified = 0x100;

        public static PasswordMessage Parse(byte[] packet) => new PasswordMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 12));

        public static byte[] Build(uint request, uint password, uint newPassword = 0)
        {
            var packet = Packet.Create(PacketTypes.Msg2ndPsw, 16);
            BitConverter.TryWriteBytes(packet.AsSpan(4), request);
            BitConverter.TryWriteBytes(packet.AsSpan(8), password);
            BitConverter.TryWriteBytes(packet.AsSpan(12), newPassword);
            return packet;
        }
    }

    // Trade partners, 32 bytes: the other player's ID at 4, action byte at 8, online byte at
    // 9, minutes left of the trial at 10, name (16 bytes) at 16
    public sealed record TradeBuddyMessage(uint Id, byte Action, bool IsOnline, int TrialMinutes, string Name)
    {
        public const byte Request = 0, Reject = 1, Online = 2, Offline = 3, Break = 4, Add = 5;

        public static TradeBuddyMessage Parse(byte[] packet) => new TradeBuddyMessage(
            BitConverter.ToUInt32(packet, 4), packet[8], packet[9] != 0, BitConverter.ToInt32(packet, 10),
            Packet.ReadText(packet, 16, 16));

        public static byte[] Build(byte action, uint id)
        {
            var packet = Packet.Create(PacketTypes.MsgTradeBuddy, 32);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            packet[8] = action;
            return packet;
        }
    }

    // A trade partner's details: ID at 4, mesh at 8, level at 12, class at 13, PK points at
    // 14, guild ID at 16 and rank at 20, spouse (16 bytes) at 26
    public sealed record TradeBuddyInfo(uint Id, uint Mesh, byte Level, byte Class, ushort PkPoints, uint GuildId, string Spouse)
    {
        public static TradeBuddyInfo Parse(byte[] packet) => new TradeBuddyInfo(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8), packet[12], packet[13],
            BitConverter.ToUInt16(packet, 14), BitConverter.ToUInt32(packet, 16), Packet.ReadText(packet, 26, 16));
    }

    // An item in someone's booth: item ID, booth ID, price, type, amount and its limit,
    // then 1 for silver or 3 for Conquer Points, and the position at 26. An item another
    // player wears comes the same way with view 4 and that player's ID for the booth's.
    public sealed record BoothItem(uint Id, uint BoothId, uint Price, uint TypeId, ushort Amount, ushort AmountLimit, ushort View)
    {
        public const ushort Silver = 1, ConquerPoints = 3, Worn = 4;

        public ushort Position { get; init; }

        public static BoothItem Parse(byte[] packet) => new BoothItem(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt16(packet, 20), BitConverter.ToUInt16(packet, 22),
            BitConverter.ToUInt16(packet, 24))
        {
            Position = BitConverter.ToUInt16(packet, 26),
        };
    }

    // An item or character ID, a value (a slot when equipping), the action, a timestamp and
    // an argument at 20 (the amount when buying; shops put their NPC ID in the first field).
    // DisplayGears carries the item IDs worn from offset 32: slots 1 to 9, the right and left
    // accessories, the steed armor and the crop
    public sealed record ItemMessage(uint Id, uint Data, uint Action, uint[] Gears)
    {
        public const uint Buy = 1, Sell = 2, Remove = 3, Equip = 4, Equipped = 5, Unequip = 6,
            StoredSilver = 9, StoreSilver = 10, TakeSilver = 11, Ping = 27, Drop = 37,
            DropSilver = 38, DisplayGears = 46;

        public const uint Repair = 14, Enchant = 28;

        // Booths: viewing has the booth's ID first; adding has the item and its price,
        // removing and buying the item and the booth's ID
        public const uint BoothView = 21, BoothAdd = 22, BoothRemove = 23, BoothBuy = 24, BoothAddCps = 29;

        public static ItemMessage Parse(byte[] packet) => new ItemMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12),
            Enumerable.Range(0, 13).Select(i => BitConverter.ToUInt32(packet, 32 + 4 * i)).ToArray());

        public static byte[] Build(uint id, uint data, uint action, uint argument = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgItem, 88);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            BitConverter.TryWriteBytes(packet.AsSpan(8), data);
            BitConverter.TryWriteBytes(packet.AsSpan(12), action);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            BitConverter.TryWriteBytes(packet.AsSpan(20), argument);
            return packet;
        }
    }

    // Warehouse contents: the NPC at 4, the action at 8, an item ID at 16, the item count at
    // 20, then 52 bytes per item with its ID and type first
    public sealed record PackageMessage(uint NpcId, byte Action, uint ItemId, List<(uint Id, uint TypeId)> Items)
    {
        public const byte Query = 0, Deposit = 1, Withdraw = 2;

        public static PackageMessage Parse(byte[] packet)
        {
            var items = new List<(uint, uint)>();
            for (int i = 0, offset = 24; i < packet[20] && offset + 8 <= packet.Length; i++, offset += 52)
                items.Add((BitConverter.ToUInt32(packet, offset), BitConverter.ToUInt32(packet, offset + 4)));
            return new PackageMessage(BitConverter.ToUInt32(packet, 4), packet[8], BitConverter.ToUInt32(packet, 16), items);
        }

        public static byte[] Build(uint npcId, byte action, uint itemId = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgPackage, 24);
            BitConverter.TryWriteBytes(packet.AsSpan(4), npcId);
            packet[8] = action;
            packet[9] = 10;
            BitConverter.TryWriteBytes(packet.AsSpan(16), itemId);
            return packet;
        }
    }

    // A ground item's ID, its item type, cell, color and the action (1 shown, 2 gone, 3 picked up)
    public sealed record GroundItemMessage(uint Id, uint TypeId, ushort X, ushort Y, ushort Action)
    {
        public const ushort Create = 1, Delete = 2, Pick = 3;

        public static GroundItemMessage Parse(byte[] packet) => new GroundItemMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 12), BitConverter.ToUInt16(packet, 14),
            BitConverter.ToUInt16(packet, 18));

        public static byte[] BuildPick(uint id, int x, int y)
        {
            var packet = Packet.Create(PacketTypes.MsgMapItem, 24);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            BitConverter.TryWriteBytes(packet.AsSpan(12), (ushort)x);
            BitConverter.TryWriteBytes(packet.AsSpan(14), (ushort)y);
            BitConverter.TryWriteBytes(packet.AsSpan(18), Pick);
            return packet;
        }
    }

    // ID, position, look and role, then strings from 22: none, or a booth's owner
    public sealed record NpcSpawn(uint Id, ushort X, ushort Y, ushort Lookface, ushort Role, string Name)
    {
        public static NpcSpawn Parse(byte[] packet)
        {
            var strings = Packet.ReadStrings(packet, 22);
            return new NpcSpawn(
                BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt16(packet, 12),
                BitConverter.ToUInt16(packet, 14), BitConverter.ToUInt16(packet, 16),
                BitConverter.ToUInt16(packet, 18), strings.Count > 0 ? strings[0] : null);
        }

        // A click on the NPC: its ID, then data and event 0
        public static byte[] BuildClick(uint npcId)
        {
            var packet = Packet.Create(PacketTypes.MsgNpc, 16);
            BitConverter.TryWriteBytes(packet.AsSpan(4), npcId);
            return packet;
        }
    }

    // An NPC with life: ID at 4, max life 12, life 16, X 20, Y 22, lookface 24, role 26
    public sealed record GoalSpawn(uint Id, uint MaxLife, uint Life, ushort X, ushort Y, ushort Lookface, ushort Role)
    {
        public static GoalSpawn Parse(byte[] packet) => new GoalSpawn(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt16(packet, 20), BitConverter.ToUInt16(packet, 22),
            BitConverter.ToUInt16(packet, 24), BitConverter.ToUInt16(packet, 26));
    }

    // One part of an NPC dialog: an option index at 10, the action at 11, strings from 12
    public sealed record DialogMessage(byte Option, byte Action, string Text)
    {
        public const byte Line = 1, Choice = 2, Face = 4, Show = 100, Answer = 101, NoOption = 255;

        public static DialogMessage Parse(byte[] packet)
        {
            var strings = Packet.ReadStrings(packet, 12);
            return new DialogMessage(packet[10], packet[11], strings.Count > 0 ? strings[0] : null);
        }

        public static byte[] BuildAnswer(byte option, string input = null)
        {
            var bytes = Encoding.Latin1.GetBytes(input ?? "");
            var packet = Packet.Create(PacketTypes.MsgTaskDialog, 16 + (input == null ? 0 : 1 + bytes.Length));
            packet[10] = option;
            packet[11] = Answer;
            if (input != null)
            {
                packet[12] = 1;
                packet[13] = (byte)bytes.Length;
                bytes.CopyTo(packet, 14);
            }
            return packet;
        }
    }

    public static class AllotMessage
    {
        // 24 bytes: 0 at 4, then u32 each for strength, agility, vitality and spirit from 8
        public static byte[] Build(uint strength, uint agility, uint vitality, uint spirit)
        {
            var packet = Packet.Create(PacketTypes.MsgAllot, 24);
            BitConverter.TryWriteBytes(packet.AsSpan(8), strength);
            BitConverter.TryWriteBytes(packet.AsSpan(12), agility);
            BitConverter.TryWriteBytes(packet.AsSpan(16), vitality);
            BitConverter.TryWriteBytes(packet.AsSpan(20), spirit);
            return packet;
        }
    }

    // A player ID, item ID or amount at 4, the action at 8
    public sealed record TradeMessage(uint Data, uint Action)
    {
        public const uint Request = 1, Close = 2, ShowTable = 3, HideTable = 5, AddItem = 6,
            SetSilver = 7, ShowSilver = 8, Accept = 10, AddItemFailed = 11, ShowConquerPoints = 12,
            SetConquerPoints = 13;

        public static TradeMessage Parse(byte[] packet) => new TradeMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8));

        public static byte[] Build(uint action, uint data)
        {
            var packet = Packet.Create(PacketTypes.MsgTrade, 12);
            BitConverter.TryWriteBytes(packet.AsSpan(4), data);
            BitConverter.TryWriteBytes(packet.AsSpan(8), action);
            return packet;
        }
    }

    // The action at 4, a player ID at 8
    public sealed record TeamMessage(uint Action, uint Data)
    {
        public const uint Create = 0, ApplyJoin = 1, Leave = 2, AcceptInvite = 3, Invite = 4,
            AcceptJoin = 5, Dismiss = 6, Kick = 7;

        public static TeamMessage Parse(byte[] packet) => new TeamMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8));

        public static byte[] Build(uint action, uint data)
        {
            var packet = Packet.Create(PacketTypes.MsgTeam, 12);
            BitConverter.TryWriteBytes(packet.AsSpan(4), action);
            BitConverter.TryWriteBytes(packet.AsSpan(8), data);
            return packet;
        }
    }

    // Action byte at 4 (0 add), then from 8 the first member: name (16 bytes), ID, look,
    // max health and health
    public sealed record TeamMemberMessage(byte Action, string Name, uint Id, uint Look, ushort MaxHealth, ushort Health)
    {
        public static TeamMemberMessage Parse(byte[] packet) => new TeamMemberMessage(
            packet[4], Packet.ReadText(packet, 8, 16), BitConverter.ToUInt32(packet, 24),
            BitConverter.ToUInt32(packet, 28), BitConverter.ToUInt16(packet, 32),
            BitConverter.ToUInt16(packet, 34));
    }

    // The other player's ID at 4, action at 8, online at 9, name (16 bytes) at 20
    public sealed record FriendMessage(uint Id, byte Action, bool IsOnline, string Name)
    {
        public const byte Apply = 10, Accept = 11, Online = 12, Offline = 13, Break = 14,
            GetInfo = 15, EnemyOnline = 16, EnemyOffline = 17, EnemyDelete = 18, EnemyAdd = 19;

        public static FriendMessage Parse(byte[] packet) => new FriendMessage(
            BitConverter.ToUInt32(packet, 4), packet[8], packet[9] != 0, Packet.ReadText(packet, 20, 16));

        public static byte[] Build(byte action, uint id, string name = "")
        {
            var packet = Packet.Create(PacketTypes.MsgFriend, 40);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            packet[8] = action;
            Packet.WriteText(packet, 20, Encoding.Latin1.GetBytes(name), 16);
            return packet;
        }
    }

    // Who asks, before a trade, team or friend request: asker and target IDs, the asker's
    // level and battle power
    public sealed record RelationMessage(uint AskerId, uint TargetId, uint Level, uint BattlePower)
    {
        public static RelationMessage Parse(byte[] packet) => new RelationMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16));
    }

    // The arena window's buttons and dialogs, 52 bytes: action at 4, option at 8, then a
    // player: ID at 12, name (16 bytes) at 16, rank at 32, class at 36, arena points at 44,
    // level at 48
    public sealed record QualifierMessage(uint Action, uint Option, uint Id, string Name, uint Rank, uint Points, uint Level)
    {
        public const uint SignUp = 0, Quit = 1, Countdown = 2, Answer = 3, OpponentGaveUp = 4,
            BuyPoints = 5, Match = 6, StartFight = 8, Dialog = 9, EndDialog = 10;
        public const uint Accept = 1, GiveUp = 2;
        public const uint Won = 1, Lost = 3;

        public static QualifierMessage Parse(byte[] packet) => new QualifierMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 12),
            Packet.ReadText(packet, 16, 16), BitConverter.ToUInt32(packet, 32), BitConverter.ToUInt32(packet, 44),
            BitConverter.ToUInt32(packet, 48));

        public static byte[] Build(uint action, uint option = 0) =>
            Build(PacketTypes.MsgQualifyingInteractive, 52, (4, action), (8, option));

        // The arena window opening asks for these, with the 5619 client's sizes
        public static byte[] BuildDetailQuery() => Packet.Create(PacketTypes.MsgQualifyingDetailInfo, 44);
        public static byte[] BuildFightersQuery(uint page) =>
            Build(PacketTypes.MsgQualifyingFightersList, 148, (4, page), (8, 6));
        public static byte[] BuildRankQuery(ushort kind, ushort page) =>
            Build(PacketTypes.MsgQualifyingRank, 52, (4, (uint)(kind | page << 16)));
        public static byte[] BuildSeasonRankQuery() => Packet.Create(PacketTypes.MsgQualifyingSeasonRankList, 60);

        private static byte[] Build(ushort type, int length, params (int Offset, uint Value)[] fields)
        {
            var packet = Packet.Create(type, length);
            foreach (var (offset, value) in fields)
                BitConverter.TryWriteBytes(packet.AsSpan(offset), value);
            return packet;
        }
    }

    // Nobility. Action u16 at 4. Asked with a donation u64 at 8 and the currency
    // u16 at 16 (0 silver, 1 CPs), a board page at 8, or a rank at 8 for its minimum.
    // Answered with "id 0 amount total position" (donation) or "id total rank position"
    // (the player's rank) as strings at 32; the board's page u16 at 8, pages u16 at 10,
    // count at 12 and 48-byte entries from 32 (ID, look at 8, name at 12, donation u64 at 32,
    // rank at 40, position at 44); or silver still needed u64 at 8 and positions at 20 and 24.
    public sealed record PeerageMessage(ushort Action, ulong Data, List<string> Strings)
    {
        public const ushort Donate = 1, Board = 2, Info = 3, Minimum = 4;

        public static PeerageMessage Parse(byte[] packet) => new PeerageMessage(
            BitConverter.ToUInt16(packet, 4), BitConverter.ToUInt64(packet, 8),
            BitConverter.ToUInt16(packet, 4) is Donate or Info ? Packet.ReadStrings(packet, 32) : new List<string>());

        public (uint Id, ulong Total, uint Rank, int Position) Standing =>
            this.Strings[0].Split(' ') is var f
                ? (uint.Parse(f[0]), ulong.Parse(f[1]), uint.Parse(f[2]), int.Parse(f[3]))
                : default;

        // Minimum: the position the donation would give, and the current one
        public static (ulong Remaining, int Position, int Current) ParseMinimum(byte[] packet) =>
            (BitConverter.ToUInt64(packet, 8), BitConverter.ToInt32(packet, 20), BitConverter.ToInt32(packet, 24));

        public static (ushort Pages, List<(uint Id, string Name, ulong Donation, uint Rank, int Position)> Entries)
            ParseBoard(byte[] packet)
        {
            var entries = new List<(uint, string, ulong, uint, int)>();
            for (int i = 0, at = 32; i < BitConverter.ToInt32(packet, 12); i++, at += 48)
                entries.Add((BitConverter.ToUInt32(packet, at), Packet.ReadText(packet, at + 12, 16),
                    BitConverter.ToUInt64(packet, at + 32), BitConverter.ToUInt32(packet, at + 40),
                    BitConverter.ToInt32(packet, at + 44)));
            return (BitConverter.ToUInt16(packet, 10), entries);
        }

        public static byte[] Build(ushort action, ulong data, ushort currency = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgPeerage, 32);
            BitConverter.TryWriteBytes(packet.AsSpan(4), action);
            BitConverter.TryWriteBytes(packet.AsSpan(8), data);
            BitConverter.TryWriteBytes(packet.AsSpan(16), currency);
            return packet;
        }
    }

    // The player's arena details: rank at 4, status at 12 (0 not signed up, 1 waiting,
    // 2 invited, 3 fighting), total wins/losses at 16/20, today's at 24/28, total and current
    // honor at 32/36, arena points at 40
    public sealed record QualifierDetails(uint Rank, uint Status, uint WinsTotal, uint LossesTotal,
        uint WinsToday, uint LossesToday, uint HonorTotal, uint Honor, uint Points)
    {
        public static QualifierDetails Parse(byte[] packet) => new QualifierDetails(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt32(packet, 20), BitConverter.ToUInt32(packet, 24), BitConverter.ToUInt32(packet, 28),
            BitConverter.ToUInt32(packet, 32), BitConverter.ToUInt32(packet, 36), BitConverter.ToUInt32(packet, 40));
    }

    // Matches being fought: count at 12, shown at 24, 120 bytes per match from 28 with the
    // two fighters' IDs at 0 and 60
    public sealed record QualifierMatches(uint Count, List<(uint A, uint B)> Shown)
    {
        public static QualifierMatches Parse(byte[] packet)
        {
            var shown = new List<(uint, uint)>();
            for (int i = 0, at = 28; i < BitConverter.ToInt32(packet, 24); i++, at += 120)
                shown.Add((BitConverter.ToUInt32(packet, at), BitConverter.ToUInt32(packet, at + 60)));
            return new QualifierMatches(BitConverter.ToUInt32(packet, 12), shown);
        }
    }

    // A ranking page: kind u16 at 4, page u16 at 6, ranked at 8, shown at 12, 36 bytes per
    // player from 16: rank u16, name (16 bytes), kind u16, points
    public sealed record QualifierRanking(uint Total, List<(ushort Rank, string Name, uint Points)> Shown)
    {
        public static QualifierRanking Parse(byte[] packet)
        {
            var shown = new List<(ushort, string, uint)>();
            for (int i = 0, at = 16; i < BitConverter.ToInt32(packet, 12); i++, at += 36)
                shown.Add((BitConverter.ToUInt16(packet, at), Packet.ReadText(packet, at + 2, 16),
                    BitConverter.ToUInt32(packet, at + 20)));
            return new QualifierRanking(BitConverter.ToUInt32(packet, 8), shown);
        }
    }

    // Last season's top ten: count at 4, 52 bytes per player from 8: ID, name (16 bytes),
    // look, level, class, 0, rank, points, wins, losses
    public sealed record QualifierSeasonEntry(uint Id, string Name, uint Mesh, uint Level, uint Class,
        uint Rank, uint Points, uint Wins, uint Losses);

    public sealed record QualifierSeasonRanking(List<QualifierSeasonEntry> Entries)
    {
        public static QualifierSeasonRanking Parse(byte[] packet)
        {
            var entries = new List<QualifierSeasonEntry>();
            for (int i = 0, at = 8; i < BitConverter.ToInt32(packet, 4); i++, at += 52)
                entries.Add(new QualifierSeasonEntry(
                    BitConverter.ToUInt32(packet, at), Packet.ReadText(packet, at + 4, 16),
                    BitConverter.ToUInt32(packet, at + 20), BitConverter.ToUInt32(packet, at + 24),
                    BitConverter.ToUInt32(packet, at + 28), BitConverter.ToUInt32(packet, at + 36),
                    BitConverter.ToUInt32(packet, at + 40), BitConverter.ToUInt32(packet, at + 44),
                    BitConverter.ToUInt32(packet, at + 48)));
            return new QualifierSeasonRanking(entries);
        }
    }

    // The match score: IDs at 4 and 28, damage at 24 and 48
    public sealed record ArenaScore(uint IdA, uint DamageA, uint IdB, uint DamageB)
    {
        public static ArenaScore Parse(byte[] packet) => new ArenaScore(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 24),
            BitConverter.ToUInt32(packet, 28), BitConverter.ToUInt32(packet, 48));
    }

    // The stat window: ID at 4, then u32s: max life, max mana, max attack, min attack,
    // defense, magic attack, magic defense, dodge and agility; from 48 the percentages: attack,
    // magic attack, damage taken off, curse and bless. Asked for with the same 136 bytes, empty
    // but for the ID of the player whose stats are wanted.
    public sealed record StatWindow(uint Id, uint MaxHealth, uint MaxMana, uint MaxAttack, uint MinAttack,
        uint Defense, uint MagicAttack, uint MagicDefense, uint Dodge, uint Agility)
    {
        public uint AttackPercent { get; init; }
        public uint MagicPercent { get; init; }
        public uint ReducePercent { get; init; }
        public uint BlessPercent { get; init; }

        public static byte[] Build(uint id)
        {
            var packet = Packet.Create(PacketTypes.MsgPlayerAttribInfo, 136);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            return packet;
        }

        public static StatWindow Parse(byte[] packet) => new StatWindow(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt32(packet, 20), BitConverter.ToUInt32(packet, 24),
            BitConverter.ToUInt32(packet, 28), BitConverter.ToUInt32(packet, 32),
            BitConverter.ToUInt32(packet, 36), BitConverter.ToUInt32(packet, 40))
        {
            AttackPercent = BitConverter.ToUInt32(packet, 48),
            MagicPercent = BitConverter.ToUInt32(packet, 52),
            ReducePercent = BitConverter.ToUInt32(packet, 56),
            BlessPercent = BitConverter.ToUInt32(packet, 64),
        };
    }

    // Action at 4 (2 = the steed's vigor), value at 8; 36 bytes
    public sealed record DataMessage(uint Action, uint Value)
    {
        public const uint Vigor = 2;

        public static DataMessage Parse(byte[] packet) => new DataMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8));
    }

    public sealed record WalkMessage(uint Direction, uint CharacterId, uint Mode)
    {
        public static WalkMessage Parse(byte[] packet) => new WalkMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12));

        public const uint Run = 1, Ride = 9;

        // Walking directions are 0-7, riding ones 0-23; the client adds a multiple of either
        public static byte[] Build(uint characterId, int direction, uint mode = Run)
        {
            var packet = Packet.Create(PacketTypes.MsgWalk, 24);
            int directions = mode == Ride ? 24 : 8;
            BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)(directions * Random.Shared.Next(1, 30) + direction));
            BitConverter.TryWriteBytes(packet.AsSpan(8), characterId);
            BitConverter.TryWriteBytes(packet.AsSpan(12), mode);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            return packet;
        }
    }

    // Guild actions: action at 4, a player, guild or amount at 8, join requirements at 12,
    // 16 and 20, strings at 24
    public sealed record SyndicateMessage(uint Action, uint Data, List<string> Strings)
    {
        public const uint ApplyJoin = 1, InviteJoin = 2, Leave = 3, QueryName = 6, Ally = 7, Unally = 8,
            Enemy = 9, Unenemy = 10, DonateSilver = 11, QueryAttribute = 12, SetSyndicate = 14,
            Disband = 19, DonateConquerPoints = 20, AllyRequest = 23, SetRequirements = 24,
            Announce = 27, Promote = 28, Discharge = 30, PromotionList = 37;

        public string Text => this.Strings.Count > 0 ? this.Strings[0] : "";

        public static SyndicateMessage Parse(byte[] packet) => new SyndicateMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            packet.Length > 24 ? Packet.ReadStrings(packet, 24) : new List<string>());

        public static byte[] Build(uint action, uint data, string text = null,
            uint level = 0, uint rebirths = 0, uint professions = 0)
        {
            var bytes = text == null ? null : Encoding.Latin1.GetBytes(text);
            var packet = Packet.Create(PacketTypes.MsgSyndicate, 25 + (bytes == null ? 0 : 1 + bytes.Length));
            BitConverter.TryWriteBytes(packet.AsSpan(4), action);
            BitConverter.TryWriteBytes(packet.AsSpan(8), data);
            BitConverter.TryWriteBytes(packet.AsSpan(12), level);
            BitConverter.TryWriteBytes(packet.AsSpan(16), rebirths);
            BitConverter.TryWriteBytes(packet.AsSpan(20), professions);
            if (bytes != null)
            {
                packet[24] = 1;
                packet[25] = (byte)bytes.Length;
                bytes.CopyTo(packet, 26);
            }
            return packet;
        }
    }

    // The guild window: guild ID at 4, own silver donation at 8, fund (u64) at 12, CP fund at
    // 20, members at 24, own rank at 28, leader (16 bytes) at 32, requirements at 48, 52, 56
    public sealed record GuildInfo(uint GuildId, uint Donation, ulong Fund, uint ConquerPointsFund,
        uint Members, uint Rank, string Leader, uint RequiredLevel)
    {
        public static GuildInfo Parse(byte[] packet) => new GuildInfo(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt64(packet, 12), BitConverter.ToUInt32(packet, 20),
            BitConverter.ToUInt32(packet, 24), BitConverter.ToUInt32(packet, 28),
            Packet.ReadText(packet, 32, 16), BitConverter.ToUInt32(packet, 48));
    }

    // One page of guild members: first index at 8, count at 12, 48-byte entries from 16 with
    // the name, level at 24, rank at 28, donation at 36, online at 40
    public sealed record GuildMember(string Name, uint Level, uint Rank, uint Donation, bool Online)
    {
        public static byte[] BuildQuery(uint startIndex)
        {
            var packet = Packet.Create(PacketTypes.MsgSynMemberList, 20);
            BitConverter.TryWriteBytes(packet.AsSpan(8), startIndex);
            return packet;
        }

        public static List<GuildMember> ParseList(byte[] packet)
        {
            var members = new List<GuildMember>();
            int count = BitConverter.ToInt32(packet, 12);
            for (int i = 0, at = 16; i < count; i++, at += 48)
                members.Add(new GuildMember(Packet.ReadText(packet, at, 16),
                    BitConverter.ToUInt32(packet, at + 24), BitConverter.ToUInt32(packet, at + 28),
                    BitConverter.ToUInt32(packet, at + 36), BitConverter.ToUInt32(packet, at + 40) != 0));
            return members;
        }
    }

    // Clan actions: action at 4, a player, clan or amount at 8, strings at 16; the member
    // list has a count at 16 and 36-byte entries (name, level at 16, rank u16 at 20, online
    // u16 at 22), the ally and enemy lists a count and 56-byte entries (clan ID, name at 4)
    public sealed record FamilyMessage(uint Action, uint Data, List<string> Strings)
    {
        public const uint Query = 1, QueryMembers = 4, Recruit = 9, AcceptRecruit = 10, Join = 11,
            AcceptJoin = 12, EnemyList = 13, AddEnemy = 14, DeleteEnemy = 15, AllyList = 16,
            AddAlly = 17, AcceptAlly = 18, DeleteAlly = 20, Abdicate = 21, Kick = 22, Quit = 23,
            Announce = 24, SetAnnouncement = 25, Donate = 26, QueryOccupy = 29;
        public const uint Leader = 100, Member = 10;

        public List<(string Name, uint Level, uint Rank, bool Online)> Members { get; init; }
        public List<(uint Id, string Name)> Clans { get; init; }

        public string Text => this.Strings.Count > 0 ? this.Strings[0] : "";

        public static FamilyMessage Parse(byte[] packet)
        {
            uint action = BitConverter.ToUInt32(packet, 4);
            uint data = BitConverter.ToUInt32(packet, 8);
            if (action == QueryMembers)
            {
                var members = new List<(string, uint, uint, bool)>();
                for (int i = 0, at = 20; i < BitConverter.ToInt32(packet, 16); i++, at += 36)
                    members.Add((Packet.ReadText(packet, at, 16), BitConverter.ToUInt32(packet, at + 16),
                        BitConverter.ToUInt16(packet, at + 20), BitConverter.ToUInt16(packet, at + 22) != 0));
                return new FamilyMessage(action, data, new List<string>()) { Members = members };
            }
            if (action == AllyList || action == EnemyList)
            {
                var clans = new List<(uint, string)>();
                for (int i = 0, at = 20; i < BitConverter.ToInt32(packet, 16); i++, at += 56)
                    clans.Add((BitConverter.ToUInt32(packet, at), Packet.ReadText(packet, at + 4, 16)));
                return new FamilyMessage(action, data, new List<string>()) { Clans = clans };
            }
            return new FamilyMessage(action, data,
                packet.Length > 16 ? Packet.ReadStrings(packet, 16) : new List<string>());
        }

        public static byte[] Build(uint action, uint data, string text = null)
        {
            var bytes = text == null ? null : Encoding.Latin1.GetBytes(text);
            var packet = Packet.Create(PacketTypes.MsgFamily, 17 + (bytes == null ? 0 : 1 + bytes.Length));
            BitConverter.TryWriteBytes(packet.AsSpan(4), action);
            BitConverter.TryWriteBytes(packet.AsSpan(8), data);
            if (bytes != null)
            {
                packet[16] = 1;
                packet[17] = (byte)bytes.Length;
                bytes.CopyTo(packet, 18);
            }
            return packet;
        }
    }

    public static class Packet
    {
        public static byte[] Create(ushort type, int length)
        {
            var packet = new byte[length];
            BitConverter.TryWriteBytes(packet.AsSpan(0), (ushort)length);
            BitConverter.TryWriteBytes(packet.AsSpan(2), type);
            return packet;
        }

        public static List<string> ReadStrings(byte[] packet, int offset)
        {
            var strings = new List<string>();
            int count = packet[offset++];
            for (int i = 0; i < count && offset < packet.Length; i++)
            {
                int length = packet[offset++];
                strings.Add(Encoding.Latin1.GetString(packet, offset, length));
                offset += length;
            }
            return strings;
        }

        public static void WriteText(byte[] packet, int offset, byte[] text, int size)
        {
            if (text.Length > size)
                throw new ArgumentException("Text longer than its field", nameof(text));
            text.CopyTo(packet, offset);
        }

        public static string ReadText(byte[] packet, int offset, int size)
        {
            int end = Array.IndexOf(packet, (byte)0, offset, size);
            return Encoding.Latin1.GetString(packet, offset, (end < 0 ? offset + size : end) - offset);
        }
    }

    // Heaven blessing EXP to claim, 16 bytes: action byte at 4 (0 amounts, 1/2 claim online
    // training / blessed hunting, 3/4 a pool is full), online u32 at 8, hunting u32 at 12;
    // 6,000,000 is an EXP ball
    public sealed record GodExpMessage(byte Action, uint Online, uint Hunting)
    {
        public const byte Query = 0, ClaimOnline = 1, ClaimHunting = 2, OnlineFull = 3, HuntingFull = 4;
        public const uint Ball = 6_000_000;

        public static GodExpMessage Parse(byte[] packet) => new GodExpMessage(
            packet[4], BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 12));

        public static byte[] Build(byte action)
        {
            var packet = Packet.Create(PacketTypes.MsgGodExp, 16);
            packet[4] = action;
            return packet;
        }
    }

    // Offline training, 12 bytes: action u16 at 4 (1 enter, 2 ask for the result, 3 result,
    // 4 claim), data u32 at 8
    public sealed record TrainingMessage(ushort Action, uint Data)
    {
        public const ushort Query = 0, Enter = 1, AskResult = 2, Result = 3, Claim = 4;

        public static TrainingMessage Parse(byte[] packet) =>
            new TrainingMessage(BitConverter.ToUInt16(packet, 4), BitConverter.ToUInt32(packet, 8));

        public static byte[] Build(ushort action)
        {
            var packet = Packet.Create(PacketTypes.MsgTraining, 12);
            BitConverter.TryWriteBytes(packet.AsSpan(4), action);
            return packet;
        }
    }

    // The offline training result, 16 bytes: minutes trained u16 at 4, left u16 at 6, level
    // after claiming u16 at 8, progress into it in ten-millionths u32 at 12
    public sealed record TrainingInfo(ushort Trained, ushort Left, ushort Level, uint Progress)
    {
        public static TrainingInfo Parse(byte[] packet) => new TrainingInfo(
            BitConverter.ToUInt16(packet, 4), BitConverter.ToUInt16(packet, 6),
            BitConverter.ToUInt16(packet, 8), BitConverter.ToUInt32(packet, 12));
    }

    // A monk's aura: action at 4, owner 8, aura 12, level 16, range 20, power 24
    public sealed record AuraMessage(ushort Action, uint Owner, uint Aura, uint Level, uint Range, uint Power)
    {
        public const ushort Add = 0, Remove = 1, Ask = 2, Got = 3, Lost = 5;

        public static AuraMessage Parse(byte[] packet) => new AuraMessage(BitConverter.ToUInt16(packet, 4),
            BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 12), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt32(packet, 20), BitConverter.ToUInt32(packet, 24));

        public static byte[] BuildAsk(uint owner, uint aura)
        {
            var packet = Packet.Create(PacketTypes.MsgAura, 28);
            BitConverter.TryWriteBytes(packet.AsSpan(4), Ask);
            BitConverter.TryWriteBytes(packet.AsSpan(8), owner);
            BitConverter.TryWriteBytes(packet.AsSpan(12), aura);
            return packet;
        }
    }
}

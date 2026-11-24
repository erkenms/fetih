namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Comet.Game.Items;

    public enum TotemPoleType : byte
    {
        Headgear = 0,
        Armor = 1,
        Weapon = 2,
        Ring = 3,
        Boots = 4,
        Necklace = 5,
        Fan = 6,
        Tower = 7,
    }

    // Donation weights from the client's ini/totempole.ini ([Quality], [Add], [Hole]) and
    // arsenal rules from the 5619 client and its help pages.
    public sealed class ArsenalConfig
    {
        public const int PoleCount = 8;
        public const int TopPolesCount = 5;
        public const int MaxPoleBattlePower = 3;
        public const int EnhancementDays = 30;
        public const ulong OverlordThreshold = 5_000_000;

        public uint EliteQuality { get; }
        public uint SuperQuality { get; }
        public IReadOnlyList<uint> PlusValues { get; }
        public uint OneSocket { get; }
        public uint TwoSockets { get; }

        public static ArsenalConfig Default { get; } = new ArsenalConfig(
            eliteQuality: 1_000,
            superQuality: 16_660,
            plusValues: new uint[]
            {
                0, 90, 490, 1_350, 4_070, 12_340, 37_030,
                111_110, 333_330, 1_000_000, 1_033_330, 1_101_230, 1_212_340,
            },
            oneSocket: 33_330,
            twoSockets: 133_330);

        public ArsenalConfig(
            uint eliteQuality, uint superQuality, uint[] plusValues, uint oneSocket, uint twoSockets)
        {
            this.EliteQuality = eliteQuality;
            this.SuperQuality = superQuality;
            this.PlusValues = plusValues;
            this.OneSocket = oneSocket;
            this.TwoSockets = twoSockets;
        }

        public static ArsenalConfig Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "totempole.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/totempole.ini in {clientDirectory}", path);
            return Parse(File.ReadAllText(path));
        }

        public static ArsenalConfig Parse(string text)
        {
            uint elite = Default.EliteQuality;
            uint super = Default.SuperQuality;
            var plus = new uint[13];
            for (int i = 0; i < Default.PlusValues.Count && i < plus.Length; i++)
                plus[i] = Default.PlusValues[i];
            uint hole1 = Default.OneSocket;
            uint hole2 = Default.TwoSockets;

            string section = string.Empty;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '#')
                    continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    section = line[1..^1].Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line[..eq].Trim();
                var valStr = line[(eq + 1)..].Trim();
                if (!uint.TryParse(valStr, out uint value))
                    continue;

                if (section.Equals("Quality", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.Equals("fine", StringComparison.OrdinalIgnoreCase))
                        elite = value;
                    else if (key.Equals("masterwork", StringComparison.OrdinalIgnoreCase))
                        super = value;
                }
                else if (section.Equals("Add", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(key, out int level) && level >= 1 && level < plus.Length)
                        plus[level] = value;
                }
                else if (section.Equals("Hole", StringComparison.OrdinalIgnoreCase))
                {
                    if (key == "1")
                        hole1 = value;
                    else if (key == "2")
                        hole2 = value;
                }
            }
            return new ArsenalConfig(elite, super, plus, hole1, hole2);
        }

        // Only Elite (8) and Super (9) equipment can be inscribed
        public uint DonationOf(uint typeID, byte plus, byte gem1, byte gem2)
        {
            int quality = ItemSlots.Quality(typeID);
            if (quality is not (8 or 9))
                return 0;
            uint total = quality == 9 ? this.SuperQuality : this.EliteQuality;
            if (plus > 0 && plus < this.PlusValues.Count)
                total += this.PlusValues[plus];
            int sockets = (gem1 != 0 ? 1 : 0) + (gem2 != 0 ? 1 : 0);
            if (sockets == 1)
                total += this.OneSocket;
            else if (sockets == 2)
                total += this.TwoSockets;
            return total;
        }

        public static uint ItemBattlePower(uint typeID, byte plus, byte gem1, byte gem2)
        {
            uint bp = ItemSlots.Quality(typeID) switch
            {
                6 => 1u,
                7 => 2u,
                8 => 3u,
                9 => 4u,
                _ => 0u,
            };
            bp += plus;
            if (gem1 != 0)
            {
                bp++;
                if (gem1 != Gems.EmptySocket && gem1 % 10 == 3)
                    bp++;
            }
            if (gem2 != 0)
            {
                bp++;
                if (gem2 != Gems.EmptySocket && gem2 % 10 == 3)
                    bp++;
            }
            return bp;
        }

        public static TotemPoleType? PoleTypeOf(uint typeID)
        {
            uint sort = typeID / 1000;
            if (sort == 201)
                return TotemPoleType.Fan;
            if (sort == 202)
                return TotemPoleType.Tower;
            if (ItemSlots.IsArrow(typeID))
                return null;
            if (ItemSlots.Fits(typeID, ItemPosition.Headwear))
                return TotemPoleType.Headgear;
            if (ItemSlots.Fits(typeID, ItemPosition.Armor))
                return TotemPoleType.Armor;
            if (ItemSlots.IsOneHanded(typeID) || ItemSlots.IsTwoHanded(typeID) || ItemSlots.IsShield(typeID))
                return TotemPoleType.Weapon;
            if (ItemSlots.Fits(typeID, ItemPosition.Ring))
                return TotemPoleType.Ring;
            if (ItemSlots.Fits(typeID, ItemPosition.Boots))
                return TotemPoleType.Boots;
            if (ItemSlots.Fits(typeID, ItemPosition.Necklace))
                return TotemPoleType.Necklace;
            return null;
        }

        // 1-based ordinal of the arsenal being unlocked
        public static ulong UnlockCost(int ordinal) => ordinal switch
        {
            1 or 2 => 5_000_000ul,
            3 or 4 or 5 => 10_000_000ul,
            6 or 7 => 15_000_000ul,
            8 => 20_000_000ul,
            _ => 20_000_000ul,
        };

        public static ulong EnhanceCost(int addBattlePower) => addBattlePower switch
        {
            1 => 60_000_000ul,
            2 => 100_000_000ul,
            _ => 0ul,
        };

        public static byte DonationBattlePower(ulong donation) =>
            donation >= 10_000_000 ? (byte)3 :
            donation >= 4_000_000 ? (byte)2 :
            donation >= 2_000_000 ? (byte)1 :
            (byte)0;

        public static bool IsOverlord(ulong donation) => donation >= OverlordThreshold;

        public static uint SharedBattlePower(uint totalBattlePower, GuildRank rank) => rank switch
        {
            GuildRank.Leader => totalBattlePower,
            GuildRank.DeputyLeader => totalBattlePower * 90 / 100,
            GuildRank.Member => totalBattlePower * 20 / 100,
            _ => 0u,
        };
    }
}

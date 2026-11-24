namespace Comet.Game.Items
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    public sealed class ItemTypeTable
    {
        private readonly Dictionary<uint, ItemType> Types;

        public int Count => this.Types.Count;

        // Records the file has but that can't be read
        public int Skipped { get; }

        public ItemTypeTable(IEnumerable<ItemType> types)
        {
            this.Types = new Dictionary<uint, ItemType>();
            foreach (var type in types)
                this.Types[type.ID] = type;
        }

        private ItemTypeTable(Dictionary<uint, ItemType> types, int skipped)
        {
            this.Types = types;
            this.Skipped = skipped;
        }

        public bool TryGet(uint id, out ItemType type) => this.Types.TryGetValue(id, out type);

        public IEnumerable<ItemType> All => this.Types.Values;

        public static ItemTypeTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "itemtype.dat");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/itemtype.dat in {clientDirectory}", path);
            var data = File.ReadAllBytes(path);
            TqFileCipher.Decrypt(data);
            return Parse(data);
        }

        // Decrypted, one record per line: fields end in "@@", with '~' for a space in text.
        // The first 48 fields are read, and the kind and description at 53 and 54.
        public static ItemTypeTable Parse(byte[] decrypted)
        {
            var types = new Dictionary<uint, ItemType>();
            int skipped = 0;
            foreach (var line in Encoding.Latin1.GetString(decrypted).Split('\n'))
            {
                var record = line.TrimEnd('\r');
                if (record.Length == 0)
                    continue;
                var type = ParseRecord(record.Split("@@"));
                if (type == null || types.ContainsKey(type.ID))
                    skipped++;
                else
                    types[type.ID] = type;
            }
            return new ItemTypeTable(types, skipped);
        }

        private static ItemType ParseRecord(string[] f)
        {
            if (f.Length < 48)
                return null;
            try
            {
                return new ItemType
                {
                    ID = uint.Parse(f[0], CultureInfo.InvariantCulture),
                    Name = f[1].Replace('~', ' '),
                    RequiredProfession = Byte(f[2]),
                    RequiredWeaponSkill = Byte(f[3]),
                    RequiredLevel = Byte(f[4]),
                    RequiredGender = Byte(f[5]),
                    RequiredStrength = UShort(f[6]),
                    RequiredAgility = UShort(f[7]),
                    RequiredVitality = UShort(f[8]),
                    RequiredSpirit = UShort(f[9]),
                    Monopoly = Byte(f[10]),
                    Price = UInt(f[12]),
                    ActionID = UInt(f[13]),
                    MaxAttack = UShort(f[14]),
                    MinAttack = UShort(f[15]),
                    Defense = UShort(f[16]),
                    Dexterity = UShort(f[17]),
                    Dodge = UShort(f[18]),
                    Life = UShort(f[19]),
                    Mana = UShort(f[20]),
                    Amount = UShort(f[21]),
                    AmountLimit = UShort(f[22]),
                    Gem1 = Byte(f[24]),
                    Gem2 = Byte(f[25]),
                    MagicAttack = UShort(f[30]),
                    MagicDefense = UShort(f[31]),
                    AttackRange = UShort(f[32]),
                    AttackSpeed = UShort(f[33]),
                    EmoneyPrice = UInt(f[37]),
                    StackLimit = UInt(f[47]),
                    Kind = f.Length > 54 ? f[53].Replace('~', ' ') : "",
                    Description = f.Length > 54 ? f[54].Replace('~', ' ') : "",
                };
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                return null;
            }
        }

        private static byte Byte(string s) => byte.Parse(s, CultureInfo.InvariantCulture);
        private static ushort UShort(string s) => ushort.Parse(s, CultureInfo.InvariantCulture);
        private static uint UInt(string s) => uint.Parse(s, CultureInfo.InvariantCulture);
    }
}

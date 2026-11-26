namespace Fetih.TestClient
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public static class ItemFiles
    {
        public const uint TestPrice = 30;

        // A Shop.dat: an INI section per shop with its NPC ID, silver as money, and its items
        public static byte[] ShopDat(params (uint NpcId, uint[] Items)[] shops)
        {
            var text = new StringBuilder($"[Header]\r\nAmount={shops.Length}\r\n\r\n");
            for (int i = 0; i < shops.Length; i++)
            {
                var (npcId, items) = shops[i];
                text.Append($"[Shop{i}]\r\nID={npcId}\r\nName=Shop{i}\r\nType=1\r\nMoneyType=0\r\n");
                text.Append($"ItemAmount={items.Length}\r\n");
                for (int j = 0; j < items.Length; j++)
                    text.Append($"Item{j}={items[j]}\r\n");
                text.Append("\r\n");
            }
            return Encoding.Latin1.GetBytes(text.ToString());
        }

        // itemadd.ini: per line item type, plus, then life, attack, defense and magic values
        // (0 here), dexterity (a steed's vigor) and dodge (its speed)
        public static byte[] ItemAddIni(params (uint Type, byte Plus, ushort Life, ushort Dexterity)[] additions)
        {
            var text = new StringBuilder();
            foreach (var (type, plus, life, dexterity) in additions)
                text.Append($"{type} {plus} {life} 0 0 0 0 0 {dexterity} 0\r\n");
            return Encoding.Latin1.GetBytes(text.ToString());
        }

        // An encrypted itemtype.dat: per item one line of 61 fields, each ending in "@@".
        // Each item sets ID, name, required level, durability and its limit, and costs
        // TestPrice silver; other fields are 0.
        public static byte[] ItemTypeDat(params (uint Id, string Name, ushort Amount, byte Level)[] items) =>
            ItemTypeDat(items, new (uint, string, ushort, ushort)[0]);

        // Items with HP (life) and MP (mana), with durability 1: what potions restore and
        // what bottles add. Dexterity, when given, is by item ID: a steed's or crop's vigor;
        // texts the kind line and description (fields 53 and 54), stackLimits how many of the
        // item one slot holds (field 47).
        public static byte[] ItemTypeDat(
            (uint Id, string Name, ushort Amount, byte Level)[] items,
            (uint Id, string Name, ushort Life, ushort Mana)[] potions,
            IReadOnlyDictionary<uint, ushort> dexterity = null,
            IReadOnlyDictionary<uint, (string Kind, string Description)> texts = null,
            IReadOnlyDictionary<uint, ushort> stackLimits = null)
        {
            ushort StackLimit(uint id) => stackLimits != null && stackLimits.TryGetValue(id, out var s) ? s : (ushort)0;
            var text = new StringBuilder();
            foreach (var (id, name, amount, level) in items)
                text.Append(Record(id, name, amount, level, 0, 0,
                    dexterity != null && dexterity.TryGetValue(id, out var d) ? d : (ushort)0,
                    texts != null && texts.TryGetValue(id, out var t) ? t : ("None", "None"), StackLimit(id)));
            foreach (var (id, name, life, mana) in potions)
                text.Append(Record(id, name, 1, 0, life, mana, 0, stackLimit: StackLimit(id)));
            return Encrypt(Encoding.Latin1.GetBytes(text.ToString()));
        }

        private static string Record(uint id, string name, ushort amount, byte level, ushort life, ushort mana,
            ushort dexterity, (string Kind, string Description) text = default, ushort stackLimit = 0)
        {
            var fields = Enumerable.Repeat("0", 61).ToArray();
            fields[0] = id.ToString();
            fields[1] = name.Replace(' ', '~');
            fields[4] = level.ToString();
            fields[12] = TestPrice.ToString();
            fields[17] = dexterity.ToString();
            fields[19] = life.ToString();
            fields[20] = mana.ToString();
            fields[21] = fields[22] = amount.ToString();
            fields[47] = stackLimit.ToString();
            fields[53] = (text.Kind ?? "None").Replace(' ', '~');
            fields[54] = (text.Description ?? "None").Replace(' ', '~');
            return string.Join("@@", fields) + "@@\r\n";
        }

        // XOR with a 128-byte key from MSVC rand() seeded with 0x2537 (or a custom seed such as
        // 0x4D2 for levexp.dat), after rotating each byte left by its offset modulo 8
        public static byte[] Encrypt(byte[] data, uint seed = 0x2537)
        {
            var key = new List<byte>();
            for (int i = 0; i < 128; i++)
            {
                seed = seed * 214013 + 2531011;
                key.Add((byte)((seed >> 16) & 0x7FFF));
            }

            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                int shift = i % 8;
                int b = data[i];
                result[i] = (byte)(((b << shift) | (b >> (8 - shift))) ^ key[i % 128]);
            }
            return result;
        }
    }
}

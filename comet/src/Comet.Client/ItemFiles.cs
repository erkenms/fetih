namespace Comet.Client
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

        // An encrypted itemtype.dat: per item one line of 61 fields, each ending in "@@".
        // Each item sets ID, name, required level, durability and its limit, and costs
        // TestPrice silver; other fields are 0.
        public static byte[] ItemTypeDat(params (uint Id, string Name, ushort Amount, byte Level)[] items)
        {
            var text = new StringBuilder();
            foreach (var (id, name, amount, level) in items)
            {
                var fields = Enumerable.Repeat("0", 61).ToArray();
                fields[0] = id.ToString();
                fields[1] = name.Replace(' ', '~');
                fields[4] = level.ToString();
                fields[12] = TestPrice.ToString();
                fields[21] = fields[22] = amount.ToString();
                fields[53] = fields[54] = "None";
                text.Append(string.Join("@@", fields)).Append("@@\r\n");
            }
            return Encrypt(Encoding.Latin1.GetBytes(text.ToString()));
        }

        // XOR with a 128-byte key from MSVC rand() seeded with 0x2537, after rotating each
        // byte left by its offset modulo 8
        public static byte[] Encrypt(byte[] data)
        {
            var key = new List<byte>();
            uint seed = 0x2537;
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

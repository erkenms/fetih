namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    public sealed record Shop(uint NpcID, byte MoneyType, IReadOnlyList<uint> Items)
    {
        public const byte Silver = 0;

        public bool Sells(uint typeID)
        {
            foreach (var item in this.Items)
                if (item == typeID)
                    return true;
            return false;
        }
    }

    // What each shopkeeper sells, read from the client's ini/Shop.dat: the client draws the
    // shop window from the same file, so the server must agree with it
    public sealed class ShopTable
    {
        private readonly Dictionary<uint, Shop> ByNpc = new Dictionary<uint, Shop>();

        public ShopTable(IEnumerable<Shop> shops)
        {
            foreach (var shop in shops)
                this.ByNpc.TryAdd(shop.NpcID, shop);
        }

        public int Count => this.ByNpc.Count;

        public Shop Find(uint npcID) => this.ByNpc.TryGetValue(npcID, out var shop) ? shop : null;

        public static ShopTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "Shop.dat");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/Shop.dat in {clientDirectory}", path);
            return Parse(File.ReadAllText(path, Encoding.Latin1));
        }

        // INI sections [ShopN] with ID (the NPC), MoneyType, ItemAmount and Item0..ItemN-1.
        // Some NPC IDs have a second section with a Chinese name and the same items; the
        // first one counts.
        public static ShopTable Parse(string text)
        {
            var shops = new List<Shop>();
            Dictionary<string, string> section = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    Add(shops, section);
                    section = line.StartsWith("[Shop") ? new Dictionary<string, string>() : null;
                    continue;
                }
                int equals = line.IndexOf('=');
                if (section != null && equals > 0)
                    section[line[..equals]] = line[(equals + 1)..];
            }
            Add(shops, section);
            return new ShopTable(shops);
        }

        private static void Add(List<Shop> shops, Dictionary<string, string> section)
        {
            if (section == null || !TryNumber(section, "ID", out var id))
                return;
            TryNumber(section, "MoneyType", out var money);
            TryNumber(section, "ItemAmount", out var amount);
            var items = new List<uint>();
            for (int i = 0; i < amount; i++)
                if (TryNumber(section, "Item" + i, out var type))
                    items.Add(type);
            shops.Add(new Shop(id, (byte)money, items));
        }

        private static bool TryNumber(Dictionary<string, string> section, string key, out uint value)
        {
            value = 0;
            return section.TryGetValue(key, out var text) &&
                uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}

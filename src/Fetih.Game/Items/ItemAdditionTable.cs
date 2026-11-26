namespace Fetih.Game.Items
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // What a "+N" adds to an item, from the client's ini/itemadd.ini (cq_itemaddition).
    // For steeds Dexterity is the vigor and Speed the riding speed in percent.
    public sealed record ItemAddition(
        uint TypeID, byte Level, ushort Life, ushort MaxAttack, ushort MinAttack, ushort Defense,
        ushort MagicAttack, ushort MagicDefense, ushort Dexterity, ushort Speed);

    public sealed class ItemAdditionTable
    {
        private readonly Dictionary<(uint, byte), ItemAddition> Additions = new Dictionary<(uint, byte), ItemAddition>();

        public ItemAdditionTable(IEnumerable<ItemAddition> additions)
        {
            foreach (var addition in additions)
                this.Additions.TryAdd((addition.TypeID, addition.Level), addition);
        }

        public int Count => this.Additions.Count;

        public ItemAddition Find(uint typeID, byte level) =>
            this.Additions.TryGetValue((typeID, level), out var addition) ? addition : null;

        public ItemAddition Of(uint typeID, byte level) => this.Find(Key(typeID), level);

        // Keys have no quality digit. One-handed weapons but backswords (421) share 444xxx,
        // two-handed ones but bows (500) and scythes (511) 555xxx.
        public static uint Key(uint typeID)
        {
            uint sort = typeID / 1000, key = typeID / 10 * 10;
            if (sort is >= 400 and < 500 and not 421)
                return 444000 + key % 1000;
            if (sort is >= 500 and < 600 and not 500 and not 511)
                return 555000 + key % 1000;
            return key;
        }

        public static ItemAdditionTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "itemadd.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/itemadd.ini in {clientDirectory}", path);
            return Parse(File.ReadAllText(path, Encoding.Latin1));
        }

        // One line per item type and level: type, level, life, max attack, min attack,
        // defense, magic attack, magic defense, dexterity, dodge (speed for steeds)
        public static ItemAdditionTable Parse(string text)
        {
            var additions = new List<ItemAddition>();
            foreach (var line in text.Split('\n'))
            {
                var f = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 10 || !uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var type))
                    continue;
                if (!byte.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var level))
                    continue;
                var values = new ushort[8];
                bool valid = true;
                for (int i = 0; i < values.Length && valid; i++)
                    valid = ushort.TryParse(f[i + 2], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]);
                if (valid)
                    additions.Add(new ItemAddition(type, level, values[0], values[1], values[2], values[3],
                        values[4], values[5], values[6], values[7]));
            }
            return new ItemAdditionTable(additions);
        }
    }
}

namespace Comet.Game.Combat
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;
    using Comet.Game.Items;
    using Comet.Game.World;

    public sealed class DropEntry
    {
        [JsonPropertyName("item")] public uint ItemID { get; init; }
        [JsonPropertyName("chance")] public double Chance { get; init; }
        [JsonPropertyName("count")] public int Count { get; init; } = 1;
    }

    public sealed class MonsterDrops
    {
        [JsonPropertyName("monster")] public uint MonsterID { get; init; }
        [JsonPropertyName("entries")] public List<DropEntry> Entries { get; init; } = new List<DropEntry>();
    }

    public sealed class DropTable
    {
        private readonly Dictionary<uint, MonsterDrops> ByMonster;

        private sealed record DropsFile(List<MonsterDrops> Drops);

        public DropTable() => this.ByMonster = new Dictionary<uint, MonsterDrops>();

        public DropTable(IEnumerable<MonsterDrops> drops) =>
            this.ByMonster = drops.ToDictionary(d => d.MonsterID);

        public int Count => this.ByMonster.Count;

        public bool TryGet(uint monsterID, out MonsterDrops drops) =>
            this.ByMonster.TryGetValue(monsterID, out drops);

        public static List<MonsterDrops> Load(string path, MonsterTypeTable monsters, ItemTypeTable items)
        {
            var drops = DataFiles.Read<DropsFile>(path).Drops ?? new List<MonsterDrops>();
            var repeated = drops.GroupBy(d => d.MonsterID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists drop tables for these monsters more than once: {string.Join(", ", repeated)}");

            var unknownMonsters = drops.Where(d => !monsters.TryGet(d.MonsterID, out _)).Select(d => d.MonsterID).ToList();
            if (unknownMonsters.Count > 0)
                throw new InvalidDataException($"{path} has drop tables for unknown monsters: {string.Join(", ", unknownMonsters)}");

            foreach (var table in drops)
            {
                if (table.Entries == null)
                    continue;
                foreach (var entry in table.Entries)
                {
                    if (entry.ItemID == 0 || !items.TryGet(entry.ItemID, out _))
                        throw new InvalidDataException($"{path} has unknown item {entry.ItemID} for monster {table.MonsterID}");
                    if (entry.Chance <= 0 || entry.Chance > 100)
                        throw new InvalidDataException($"{path} has out-of-range chance {entry.Chance} for monster {table.MonsterID}");
                    if (entry.Count < 1)
                        throw new InvalidDataException($"{path} has invalid count {entry.Count} for monster {table.MonsterID}");
                }
            }

            return drops;
        }
    }
}

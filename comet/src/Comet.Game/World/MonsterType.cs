namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;

    public sealed class MonsterType
    {
        // TQ's attack_user flags
        public const uint AttacksOnSight = 0x02;
        public const uint GuardFlag = 0x08;

        [JsonPropertyName("id")] public uint ID { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; }
        [JsonPropertyName("lookface")] public uint Lookface { get; init; }
        [JsonPropertyName("level")] public byte Level { get; init; }
        [JsonPropertyName("life")] public uint Life { get; init; }
        [JsonPropertyName("attackMin")] public int AttackMin { get; init; }
        [JsonPropertyName("attackMax")] public int AttackMax { get; init; }
        [JsonPropertyName("defense")] public int Defense { get; init; }
        [JsonPropertyName("dexterity")] public int Dexterity { get; init; }
        [JsonPropertyName("dodge")] public int Dodge { get; init; }
        [JsonPropertyName("attackRange")] public int AttackRange { get; init; }
        [JsonPropertyName("viewRange")] public int ViewRange { get; init; }
        [JsonPropertyName("attackSpeed")] public int AttackSpeed { get; init; }
        [JsonPropertyName("moveSpeed")] public int MoveSpeed { get; init; }
        [JsonPropertyName("attackUser")] public uint AttackUser { get; init; }
        [JsonPropertyName("dropMoney")] public uint DropMoney { get; init; }
        [JsonPropertyName("dropHp")] public uint DropHp { get; init; }
        [JsonPropertyName("dropMp")] public uint DropMp { get; init; }
        [JsonPropertyName("boss")] public bool Boss { get; init; }

        public bool IsGuard => (this.AttackUser & GuardFlag) != 0;
        public bool IsAggressive => (this.AttackUser & AttacksOnSight) != 0;
    }

    public sealed class MonsterTypeTable
    {
        private readonly Dictionary<uint, MonsterType> Types;

        private sealed record MonsterFile(List<MonsterType> Monsters);

        public static List<MonsterType> Load(string path)
        {
            var types = DataFiles.Read<MonsterFile>(path).Monsters ?? new List<MonsterType>();
            var repeated = types.GroupBy(t => t.ID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these monster IDs more than once: {string.Join(", ", repeated)}");
            var lifeless = types.Where(t => t.Life == 0 || t.AttackMin > t.AttackMax).Select(t => t.ID).ToList();
            if (lifeless.Count > 0)
                throw new InvalidDataException($"{path} has monsters without life or with attackMin above attackMax: {string.Join(", ", lifeless)}");
            return types;
        }

        public MonsterTypeTable(IEnumerable<MonsterType> types) =>
            this.Types = types.ToDictionary(t => t.ID);

        public int Count => this.Types.Count;

        public bool TryGet(uint id, out MonsterType type) => this.Types.TryGetValue(id, out type);

        public MonsterType Find(string name) =>
            this.Types.Values.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}

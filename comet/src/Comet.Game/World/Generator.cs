namespace Comet.Game.World
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;

    public sealed class Generator
    {
        [JsonPropertyName("id")] public uint ID { get; init; }
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("x")] public ushort X { get; init; }
        [JsonPropertyName("y")] public ushort Y { get; init; }
        [JsonPropertyName("spread")] public ushort Spread { get; init; }
        [JsonPropertyName("amount")] public int Amount { get; init; }
        [JsonPropertyName("respawn")] public int RespawnSeconds { get; init; }
        [JsonPropertyName("monster")] public uint MonsterID { get; init; }

        public static bool Same(Generator a, Generator b) =>
            a.ID == b.ID && a.MapID == b.MapID && a.X == b.X && a.Y == b.Y && a.Spread == b.Spread &&
            a.Amount == b.Amount && a.RespawnSeconds == b.RespawnSeconds && a.MonsterID == b.MonsterID;

        private sealed record SpawnFile(List<Generator> Spawns);

        public static List<Generator> Load(string path, MonsterTypeTable types)
        {
            var generators = DataFiles.Read<SpawnFile>(path).Spawns ?? new List<Generator>();
            var repeated = generators.GroupBy(g => g.ID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these spawn IDs more than once: {string.Join(", ", repeated)}");
            var unknown = generators.Where(g => !types.TryGet(g.MonsterID, out _)).Select(g => g.ID).ToList();
            if (unknown.Count > 0)
                throw new InvalidDataException($"{path} has spawns of unknown monsters: {string.Join(", ", unknown)}");
            return generators;
        }
    }
}

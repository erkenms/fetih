namespace Comet.Game.World
{
    using System.Text.Json.Serialization;

    public sealed class Npc
    {
        // The role of warehousemen
        public const byte StorageRole = 3;

        [JsonPropertyName("id")] public uint NpcID { get; init; }
        [JsonPropertyName("lookface")] public ushort Lookface { get; init; }
        [JsonPropertyName("role")] public byte Role { get; init; }
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("x")] public ushort X { get; init; }
        [JsonPropertyName("y")] public ushort Y { get; init; }
    }
}

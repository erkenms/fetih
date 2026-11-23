namespace Comet.Game.World
{
    using System.Text.Json.Serialization;

    public sealed class Npc
    {
        public const byte StorageRole = 3;

        // Training ground goals: stakes are hit with weapons, scarecrows with spells
        public const byte StakeRole = 21, ScarecrowRole = 22;

        [JsonPropertyName("id")] public uint NpcID { get; init; }
        [JsonPropertyName("lookface")] public ushort Lookface { get; init; }
        [JsonPropertyName("role")] public byte Role { get; init; }
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("x")] public ushort X { get; init; }
        [JsonPropertyName("y")] public ushort Y { get; init; }

        // NPCs with life can be attacked; level is the least a player needs to
        [JsonPropertyName("life")] public uint Life { get; init; }
        [JsonPropertyName("level")] public byte Level { get; init; }

        public bool IsGoal => this.Role is StakeRole or ScarecrowRole;
    }
}

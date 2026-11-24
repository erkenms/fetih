namespace Comet.Game.World
{
    using System.Text.Json.Serialization;

    public sealed class Npc
    {
        public const byte StorageRole = 3;

        // Clicking it opens the client's avatar window
        public const byte BeauticianRole = 5;

        // Training ground goals: stakes are hit with weapons, scarecrows with spells
        public const byte StakeRole = 21, ScarecrowRole = 22;

        // Guild War structures: pole (role 10) and gates (role 26)
        public const byte PoleRole = 10, GateRole = 26;

        [JsonPropertyName("id")] public uint NpcID { get; init; }
        [JsonPropertyName("lookface")] public ushort Lookface { get; init; }
        [JsonPropertyName("role")] public byte Role { get; init; }
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("x")] public ushort X { get; init; }
        [JsonPropertyName("y")] public ushort Y { get; init; }

        // NPCs with life can be attacked; level is the least a player needs to
        [JsonPropertyName("life")] public uint Life { get; init; }
        [JsonPropertyName("level")] public byte Level { get; init; }

        public static bool Same(Npc a, Npc b) =>
            a.NpcID == b.NpcID && a.Lookface == b.Lookface && a.Role == b.Role && a.MapID == b.MapID &&
            a.X == b.X && a.Y == b.Y && a.Life == b.Life && a.Level == b.Level;

        public bool IsGoal => this.Role is StakeRole or ScarecrowRole;
        public bool IsGuildWarTarget => this.MapID == Social.GuildWar.MapID && this.Role is PoleRole or GateRole;
    }
}

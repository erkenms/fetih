namespace Comet.Game.World
{
    using System.Text.Json.Serialization;

    public sealed class Portal
    {
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("portal")] public uint PortalIndex { get; init; }
        [JsonPropertyName("toMap")] public uint TargetMapID { get; init; }
        [JsonPropertyName("toX")] public ushort TargetX { get; init; }
        [JsonPropertyName("toY")] public ushort TargetY { get; init; }
    }
}

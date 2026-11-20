namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;

    // cq_map "type" bits (Albetros 5517), sent to the client in MsgMapInfo
    [Flags]
    public enum MapFlags : ulong
    {
        None = 0,
        PkField = 1 << 0,
        ChangeMapDisable = 1 << 1,
        RecordDisable = 1 << 2,
        PkDisable = 1 << 3,
        BoothEnable = 1 << 4,
        TeamDisable = 1 << 5,
        TeleportDisable = 1 << 6,
        GuildMap = 1 << 7,
        PrisonMap = 1 << 8,
        WingDisable = 1 << 9,
        Family = 1 << 10,
        MineField = 1 << 11,
        PkGame = 1 << 12,
        NeverWound = 1 << 13,
        DeadIsland = 1 << 14,
        // Set on some maps in cq_map, meaning unknown
        Bit15 = 1 << 15,
        Bit16 = 1 << 16,
    }

    public sealed class MapType
    {
        [JsonPropertyName("map")] public uint MapID { get; init; }
        [JsonPropertyName("flags"), JsonConverter(typeof(JsonStringEnumConverter))]
        public MapFlags Flags { get; init; }
        [JsonPropertyName("reborn")] public MapPoint Reborn { get; init; }
    }

    public sealed record MapPoint(
        [property: JsonPropertyName("map")] uint MapID,
        [property: JsonPropertyName("x")] ushort X,
        [property: JsonPropertyName("y")] ushort Y);

    // Data/maps.json; maps not listed have no flags
    public sealed class MapTypes
    {
        private readonly Dictionary<uint, MapType> Maps;

        private sealed record MapTypeFile(List<MapType> Maps);

        public static List<MapType> Load(string path)
        {
            var maps = DataFiles.Read<MapTypeFile>(path).Maps ?? new List<MapType>();
            var repeated = maps.GroupBy(m => m.MapID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these maps more than once: {string.Join(", ", repeated)}");
            var homeless = maps.Where(m => m.Flags.HasFlag(MapFlags.RecordDisable) && m.Reborn == null)
                .Select(m => m.MapID).ToList();
            if (homeless.Count > 0)
                throw new InvalidDataException($"{path} has RecordDisable maps without a reborn point: {string.Join(", ", homeless)}");
            return maps;
        }

        public MapTypes(IEnumerable<MapType> maps) =>
            this.Maps = maps.ToDictionary(m => m.MapID);

        public int Count => this.Maps.Count;

        public MapFlags Of(uint mapID) => this.Maps.TryGetValue(mapID, out var map) ? map.Flags : MapFlags.None;

        // Where a player on this map is saved, or null to save where they stand
        public MapPoint LoginPointOf(uint mapID) =>
            this.Maps.TryGetValue(mapID, out var map) && map.Flags.HasFlag(MapFlags.RecordDisable) ? map.Reborn : null;
    }
}

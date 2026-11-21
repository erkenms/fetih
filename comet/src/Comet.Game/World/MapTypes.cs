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
        [JsonPropertyName("name")] public string Name { get; init; }
        [JsonPropertyName("aliases")] public List<string> Aliases { get; init; }
        [JsonPropertyName("flags"), JsonConverter(typeof(JsonStringEnumConverter))]
        public MapFlags Flags { get; init; }
        [JsonPropertyName("portal")] public Cell Portal { get; init; }
        [JsonPropertyName("reborn")] public uint? RebornMapID { get; init; }
        [JsonPropertyName("scroll")] public uint? ScrollTypeID { get; init; }
        [JsonPropertyName("ridePlus")] public byte RidePlus { get; init; }

        public IEnumerable<string> Names =>
            (this.Name == null ? Enumerable.Empty<string>() : new[] { this.Name }).Concat(this.Aliases ?? new List<string>());
    }

    public sealed record Cell(
        [property: JsonPropertyName("x")] ushort X,
        [property: JsonPropertyName("y")] ushort Y);

    public sealed record MapPoint(uint MapID, ushort X, ushort Y);

    // Maps not listed have no flags, portal or reborn point
    public sealed class MapTypes
    {
        private readonly Dictionary<uint, MapType> Maps;
        private readonly Dictionary<string, MapType> ByName = new Dictionary<string, MapType>();
        private readonly Dictionary<uint, MapType> ByScroll = new Dictionary<uint, MapType>();

        private sealed record MapTypeFile(List<MapType> Maps);

        public static List<MapType> Load(string path)
        {
            var maps = DataFiles.Read<MapTypeFile>(path).Maps ?? new List<MapType>();
            var problems = new List<string>();
            void Check(string problem, IEnumerable<object> found)
            {
                var list = found.Distinct().ToList();
                if (list.Count > 0)
                    problems.Add($"{problem}: {string.Join(", ", list)}");
            }

            Check("maps listed more than once",
                maps.GroupBy(m => m.MapID).Where(g => g.Count() > 1).Select(g => (object)g.Key));
            var portals = maps.Where(m => m.Portal != null).Select(m => m.MapID).ToHashSet();
            Check("maps reborn at a map without a portal",
                maps.Where(m => m.RebornMapID is uint r && !portals.Contains(r)).Select(m => (object)m.MapID));
            Check("RecordDisable maps without a reborn map",
                maps.Where(m => m.Flags.HasFlag(MapFlags.RecordDisable) && m.RebornMapID == null).Select(m => (object)m.MapID));
            Check("names used more than once",
                maps.SelectMany(m => m.Names).GroupBy(Normalize).Where(g => g.Count() > 1).Select(g => (object)g.Key));
            Check("names that are numbers",
                maps.SelectMany(m => m.Names).Where(n => uint.TryParse(n, out _)).Select(n => (object)n));
            Check("scrolls used more than once",
                maps.Where(m => m.ScrollTypeID != null).GroupBy(m => m.ScrollTypeID).Where(g => g.Count() > 1).Select(g => (object)g.Key));
            Check("scroll maps without a portal",
                maps.Where(m => m.ScrollTypeID != null && m.Portal == null).Select(m => (object)m.MapID));
            if (problems.Count > 0)
                throw new InvalidDataException($"{path} has {string.Join("; ", problems)}");
            return maps;
        }

        public MapTypes(IEnumerable<MapType> maps)
        {
            this.Maps = maps.ToDictionary(m => m.MapID);
            foreach (var map in this.Maps.Values)
            {
                foreach (var name in map.Names)
                    this.ByName[Normalize(name)] = map;
                if (map.ScrollTypeID is uint scroll)
                    this.ByScroll[scroll] = map;
            }
        }

        public int Count => this.Maps.Count;

        public MapFlags Of(uint mapID) => this.Maps.TryGetValue(mapID, out var map) ? map.Flags : MapFlags.None;

        // The steed plus needed to ride on the map
        public byte RidePlusOf(uint mapID) => this.Maps.TryGetValue(mapID, out var map) ? map.RidePlus : (byte)0;

        public MapPoint PortalOf(uint mapID) =>
            this.Maps.TryGetValue(mapID, out var map) && map.Portal != null
                ? new MapPoint(mapID, map.Portal.X, map.Portal.Y) : null;

        public MapPoint RebornPointOf(uint mapID) =>
            this.Maps.TryGetValue(mapID, out var map) && map.RebornMapID is uint reborn ? this.PortalOf(reborn) : null;

        // Where a player on this map is saved, or null to save where they stand
        public MapPoint LoginPointOf(uint mapID) =>
            this.Of(mapID).HasFlag(MapFlags.RecordDisable) ? this.RebornPointOf(mapID) : null;

        public MapPoint ScrollPointOf(uint itemTypeID) =>
            this.ByScroll.TryGetValue(itemTypeID, out var map) ? this.PortalOf(map.MapID) : null;

        // A map by its name or alias, case and '_', '-' aside, or by the start of exactly one
        // name ("twin" for twin_city). Null if none or several.
        public MapType Find(string name)
        {
            string key = Normalize(name);
            if (key.Length == 0)
                return null;
            if (this.ByName.TryGetValue(key, out var exact))
                return exact;
            var starts = this.Maps.Values.Where(m => m.Name != null && Normalize(m.Name).StartsWith(key)).Take(2).ToList();
            return starts.Count == 1 ? starts[0] : null;
        }

        public IEnumerable<MapType> Named => this.Maps.Values.Where(m => m.Name != null).OrderBy(m => m.MapID);

        private static string Normalize(string name) =>
            new string(name.Where(c => c != '_' && c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
    }
}

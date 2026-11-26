namespace Fetih.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    public readonly record struct Area(int X, int Y, int Width, int Height)
    {
        public bool Contains(int x, int y) => x >= this.X && x < this.X + this.Width && y >= this.Y && y < this.Y + this.Height;
        public (ushort X, ushort Y) Center => ((ushort)(this.X + this.Width / 2), (ushort)(this.Y + this.Height / 2));
    }

    // What the client knows of the Capture the Flag arena. ini/region.ini lines are
    // "map type x y width height name ...": on the arena, type 24 is the safe zone and type 25
    // the flag bases, whose entry makes the client report the hero reached one. info.ini's
    // [WarflagReward] "rankN=CPs,silver" is the prize of the guild ranked N.
    public sealed class WarFlagArena
    {
        public const int SafeZoneType = 24, BaseType = 25;

        public Area SafeZone { get; }
        public IReadOnlyList<Area> Bases { get; }
        public IReadOnlyList<(uint ConquerPoints, ulong Silver)> Prizes { get; }

        public WarFlagArena(Area safeZone, IReadOnlyList<Area> bases, IReadOnlyList<(uint, ulong)> prizes)
        {
            this.SafeZone = safeZone;
            this.Bases = bases;
            this.Prizes = prizes;
        }

        public static WarFlagArena Load(string clientDirectory, uint mapID)
        {
            string Read(string name)
            {
                var path = Path.Combine(clientDirectory, "ini", name);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"No ini/{name} in {clientDirectory}", path);
                return File.ReadAllText(path, Encoding.Latin1);
            }
            return Parse(Read("region.ini"), Read("info.ini"), mapID);
        }

        public static WarFlagArena Parse(string regionIni, string infoIni, uint mapID)
        {
            Area? safe = null;
            var bases = new List<Area>();
            foreach (var line in regionIni.Split('\n'))
            {
                var f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 6 || !uint.TryParse(f[0], out var map) || map != mapID)
                    continue;
                var n = f.Skip(1).Take(5).Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1).ToArray();
                if (n.Any(v => v < 0))
                    continue;
                var area = new Area(n[1], n[2], n[3], n[4]);
                if (n[0] == SafeZoneType)
                    safe ??= area;
                else if (n[0] == BaseType)
                    bases.Add(area);
            }
            if (safe == null || bases.Count == 0)
                throw new InvalidDataException($"ini/region.ini has no safe zone (type 24) or flag base (type 25) on map {mapID}");

            var prizes = new List<(uint, ulong)>();
            bool inSection = false;
            var byRank = new SortedDictionary<int, (uint, ulong)>();
            foreach (var raw in infoIni.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line.Equals("[WarflagReward]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection || !line.StartsWith("rank", StringComparison.OrdinalIgnoreCase))
                    continue;
                int eq = line.IndexOf('=');
                if (eq < 0 || !int.TryParse(line.AsSpan(4, eq - 4), out var rank))
                    continue;
                var parts = line.Substring(eq + 1).Split("//")[0].Split(',');
                if (parts.Length == 2 && uint.TryParse(parts[0].Trim(), out var cps) && ulong.TryParse(parts[1].Trim(), out var silver))
                    byRank[rank] = (cps, silver);
            }
            for (int rank = 1; byRank.TryGetValue(rank, out var prize); rank++)
                prizes.Add(prize);
            return new WarFlagArena(safe.Value, bases, prizes);
        }
    }
}

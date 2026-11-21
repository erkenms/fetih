namespace Comet.Game.World
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public readonly record struct PortalTarget(uint MapID, ushort X, ushort Y);

    public sealed class PortalTable
    {
        private readonly Dictionary<(uint MapID, uint Index), PortalTarget> Targets =
            new Dictionary<(uint, uint), PortalTarget>();

        private sealed record PortalFile(List<Portal> Portals);

        public static List<Portal> Load(string path)
        {
            var portals = DataFiles.Read<PortalFile>(path).Portals ?? new List<Portal>();
            var repeated = portals.GroupBy(p => (p.MapID, p.PortalIndex)).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.MapID}/{g.Key.PortalIndex}").ToList();
            if (repeated.Count > 0)
                throw new InvalidDataException($"{path} lists these map/portal pairs more than once: {string.Join(", ", repeated)}");
            return portals;
        }

        public PortalTable(IEnumerable<Portal> portals)
        {
            foreach (var portal in portals)
                this.Targets[(portal.MapID, portal.PortalIndex)] =
                    new PortalTarget(portal.TargetMapID, portal.TargetX, portal.TargetY);
        }

        public int Count => this.Targets.Count;

        public bool TryGet(uint mapID, uint index, out PortalTarget target) =>
            this.Targets.TryGetValue((mapID, index), out target);

        // Where some portal into this map lands, or null if none leads there
        public PortalTarget? Into(uint mapID)
        {
            var into = this.Targets.Where(t => t.Value.MapID == mapID).OrderBy(t => t.Key).ToList();
            return into.Count > 0 ? into[0].Value : null;
        }
    }
}

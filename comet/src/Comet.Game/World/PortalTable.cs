namespace Comet.Game.World
{
    using System.Collections.Generic;
    using Comet.Game.Database.Models;

    public readonly record struct PortalTarget(uint MapID, ushort X, ushort Y);

    public sealed class PortalTable
    {
        private readonly Dictionary<(uint MapID, uint Index), PortalTarget> Targets =
            new Dictionary<(uint, uint), PortalTarget>();

        public PortalTable(IEnumerable<DbPortal> portals)
        {
            foreach (var portal in portals)
                this.Targets[(portal.MapID, portal.PortalIndex)] =
                    new PortalTarget(portal.TargetMapID, portal.TargetX, portal.TargetY);
        }

        public int Count => this.Targets.Count;

        public bool TryGet(uint mapID, uint index, out PortalTarget target) =>
            this.Targets.TryGetValue((mapID, index), out target);
    }
}

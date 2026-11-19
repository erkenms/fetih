namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("portal")]
    public class DbPortal
    {
        public uint MapID { get; set; }
        public uint PortalIndex { get; set; }
        public uint TargetMapID { get; set; }
        public ushort TargetX { get; set; }
        public ushort TargetY { get; set; }
    }
}

namespace Fetih.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("detained_item")]
    public class DbDetainedItem
    {
        [Key]
        public uint DetainID { get; set; }
        public uint ItemID { get; set; }
        public uint TargetID { get; set; }
        public string TargetName { get; set; }
        public uint HunterID { get; set; }
        public string HunterName { get; set; }
        public uint Cost { get; set; }
        public DateTime DetainedAt { get; set; }
        public bool Redeemed { get; set; }
    }
}

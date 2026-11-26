namespace Fetih.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("flower")]
    public class DbFlower
    {
        public uint CharacterID { get; set; }
        public uint RedRoses { get; set; }
        public uint RedRosesToday { get; set; }
        public uint Lilies { get; set; }
        public uint LiliesToday { get; set; }
        public uint Orchids { get; set; }
        public uint OrchidsToday { get; set; }
        public uint Tulips { get; set; }
        public uint TulipsToday { get; set; }
        public DateTime? TodayDate { get; set; }
    }
}

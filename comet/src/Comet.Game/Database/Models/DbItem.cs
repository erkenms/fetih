namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("item")]
    public class DbItem
    {
        public uint ItemID { get; set; }
        public uint CharacterID { get; set; }
        public uint TypeID { get; set; }
        public byte Position { get; set; }
        public ushort Amount { get; set; }
        public ushort AmountLimit { get; set; }
        public byte Gem1 { get; set; }
        public byte Gem2 { get; set; }
        public byte Plus { get; set; }
        public byte Bless { get; set; }
        public byte Enchant { get; set; }
        public byte Color { get; set; }
    }
}

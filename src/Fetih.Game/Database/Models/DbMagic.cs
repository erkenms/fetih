namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("magic")]
    public class DbMagic
    {
        public uint CharacterID { get; set; }
        public ushort Type { get; set; }
        public ushort Level { get; set; }
        public uint Experience { get; set; }
        public ushort OldLevel { get; set; }
    }

    [Table("proficiency")]
    public class DbProficiency
    {
        public uint CharacterID { get; set; }
        public ushort Type { get; set; }
        public ushort Level { get; set; }
        public uint Experience { get; set; }
    }
}

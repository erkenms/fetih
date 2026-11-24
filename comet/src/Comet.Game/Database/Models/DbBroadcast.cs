namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("broadcast")]
    public class DbBroadcast
    {
        public uint ID { get; set; }
        public uint Position { get; set; }
        public uint CharacterID { get; set; }
        public string SenderName { get; set; }
        public string Message { get; set; }
        public uint Addition { get; set; }
    }
}

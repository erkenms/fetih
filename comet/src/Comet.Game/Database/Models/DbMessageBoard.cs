namespace Comet.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("message_board")]
    public class DbMessageBoard
    {
        public ushort Channel { get; set; }
        public uint CharacterID { get; set; }
        public uint SyndicateID { get; set; }
        public string AuthorName { get; set; }
        public string Message { get; set; }
        public DateTime PostedAt { get; set; }
    }
}

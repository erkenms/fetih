namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    // One row each way: a friendship is two rows
    [Table("friend")]
    public class DbFriend
    {
        public uint CharacterID { get; set; }
        public uint FriendID { get; set; }
    }
}

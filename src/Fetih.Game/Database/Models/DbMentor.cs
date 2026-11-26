namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("mentor")]
    public class DbMentor
    {
        public uint ApprenticeID { get; set; }
        public uint MentorID { get; set; }
        public uint Enrolled { get; set; }
        public uint Experience { get; set; }
        public ushort Blessing { get; set; }
        public ushort Composition { get; set; }
    }

    [Table("mentor_reward")]
    public class DbMentorReward
    {
        public uint CharacterID { get; set; }
        public uint Experience { get; set; }
        public ushort Blessing { get; set; }
        public ushort Composition { get; set; }
    }
}

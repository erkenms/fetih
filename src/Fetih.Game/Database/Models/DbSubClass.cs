namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("subclass")]
    public partial class DbSubClass
    {
        public virtual uint CharacterID { get; set; }
        public virtual byte SubClassID { get; set; }
        public virtual byte Level { get; set; } = 1;
        public virtual byte Phase { get; set; } = 1;
    }
}

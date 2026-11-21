namespace Comet.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    // One row each way: a partnership is two rows
    [Table("trade_partner")]
    public class DbTradePartner
    {
        public uint CharacterID { get; set; }
        public uint PartnerID { get; set; }
        public DateTime Since { get; set; }
    }
}

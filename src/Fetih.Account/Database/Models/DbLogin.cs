namespace Fetih.Account.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("logins")]
    public partial class DbLogin
    {
        public DateTime Timestamp { get; set; }
        public uint AccountID { get; set; }
        public string IPAddress { get; set; }

        public virtual DbAccount Account { get; set; }
    }
}

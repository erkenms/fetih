namespace Comet.Account.Database.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("account_status")]
    public partial class DbAccountStatus
    {
        public DbAccountStatus()
        {
            Account = new HashSet<DbAccount>();
        }

        public ushort StatusID { get; set; }
        public string StatusName { get; set; }

        public virtual ICollection<DbAccount> Account { get; set; }
    }

    public enum AccountStatus : ushort
    {
        Registered = 1,
        Activated = 2,
        Limited = 3,
        Locked = 4,
        Banned = 5
    }
}

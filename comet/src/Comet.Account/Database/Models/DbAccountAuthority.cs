namespace Comet.Account.Database.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("account_authority")]
    public partial class DbAccountAuthority
    {
        public DbAccountAuthority()
        {
            Account = new HashSet<DbAccount>();
            Realms = new HashSet<DbRealm>();
        }

        public ushort AuthorityID { get; set; }
        public string AuthorityName { get; set; }

        public virtual ICollection<DbAccount> Account { get; set; }
        public virtual ICollection<DbRealm> Realms { get; set; }
    }
}

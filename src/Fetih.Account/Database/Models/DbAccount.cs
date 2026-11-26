namespace Fetih.Account.Database.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("account")]
    public partial class DbAccount
    {
        public DbAccount()
        {
            Logins = new HashSet<DbLogin>();
        }

        public uint AccountID { get; private set; }
        public string Username { get; set; }
        public string SrpSalt { get; set; }
        public string SrpVerifier { get; set; }
        public ushort AuthorityID { get; set; }
        public ushort StatusID { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string IPAddress { get; set; }
        public DateTime Registered { get; set; }

        public virtual DbAccountAuthority Authority { get; set; }
        public virtual DbAccountStatus Status { get; set; }
        public virtual ICollection<DbLogin> Logins { get; set; }
    }
}

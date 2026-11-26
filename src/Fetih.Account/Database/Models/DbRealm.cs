namespace Fetih.Account.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;
    using Fetih.Network.RPC;

    [Table("realm")]
    public partial class DbRealm
    {
        public uint RealmID { get; private set; }
        public string Name { get; set; }
        public ushort AuthorityID { get; set; }
        public string GameIPAddress { get; set; }
        public uint GamePort { get; set; }
        public string RpcIPAddress { get; set; }
        public uint RpcPort { get; set; }

        public virtual DbAccountAuthority Authority { get; set; }

        [NotMapped]
        public RpcClient Rpc;
    }
}
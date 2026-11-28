namespace Fetih.Account.Database
{
    using Fetih.Account.Database.Models;
    using Fetih.Core.Database;
    using Microsoft.EntityFrameworkCore;

    public partial class ServerDbContext : DbContext
    {
        public static ServerConfiguration.DatabaseConfiguration Configuration;

        public virtual DbSet<DbAccount> Accounts { get; set; }
        public virtual DbSet<DbAccountAuthority> AccountAuthorities { get; set; }
        public virtual DbSet<DbAccountStatus> AccountStatuses { get; set; }
        public virtual DbSet<DbLogin> Logins { get; set; }
        public virtual DbSet<DbRealm> Realms { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseLazyLoadingProxies(false);
            options.UseMySql(DatabaseConnection.Build(
                ServerDbContext.Configuration.Hostname,
                ServerDbContext.Configuration.Schema,
                ServerDbContext.Configuration.Username,
                ServerDbContext.Configuration.Password),
                MySqlServerVersion.LatestSupportedServerVersion);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<DbAccount>(e => e.HasKey(x => x.AccountID));
            builder.Entity<DbAccountAuthority>(e => e.HasKey(x => x.AuthorityID));
            builder.Entity<DbAccountStatus>(e => e.HasKey(x => x.StatusID));
            builder.Entity<DbLogin>(e => e.HasKey(x => new { x.AccountID, x.Timestamp }));
            builder.Entity<DbRealm>(e => e.HasKey(x => x.RealmID));
        }

        public static bool Ping()
        {
            try 
            {
                using ServerDbContext ctx = new ServerDbContext();
                return ctx.Database.CanConnect();
            }
            catch { return false; }
        }
    }
}

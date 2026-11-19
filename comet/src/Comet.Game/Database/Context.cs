namespace Comet.Game.Database
{
    using Microsoft.EntityFrameworkCore;
    using Comet.Game.Database.Models;

    public partial class ServerDbContext : DbContext
    {
        public static ServerConfiguration.DatabaseConfiguration Configuration;

        public virtual DbSet<DbCharacter> Characters { get; set; }
        public virtual DbSet<DbItem> Items { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseLazyLoadingProxies(false);
            options.UseMySql(string.Format("server={0};database={1};user={2};password={3}",
                ServerDbContext.Configuration.Hostname, 
                ServerDbContext.Configuration.Schema,
                ServerDbContext.Configuration.Username, 
                ServerDbContext.Configuration.Password),
                MySqlServerVersion.LatestSupportedServerVersion);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<DbCharacter>(e => e.HasKey(x => x.CharacterID));
            builder.Entity<DbItem>(e => e.HasKey(x => x.ItemID));
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

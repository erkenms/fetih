namespace Comet.Game.Database
{
    using Microsoft.EntityFrameworkCore;
    using Comet.Game.Database.Models;

    public partial class ServerDbContext : DbContext
    {
        public static ServerConfiguration.DatabaseConfiguration Configuration;

        public virtual DbSet<DbCharacter> Characters { get; set; }
        public virtual DbSet<DbItem> Items { get; set; }
        public virtual DbSet<DbFriend> Friends { get; set; }
        public virtual DbSet<DbEnemy> Enemies { get; set; }
        public virtual DbSet<DbTradePartner> TradePartners { get; set; }
        public virtual DbSet<DbMagic> Magics { get; set; }
        public virtual DbSet<DbProficiency> Proficiencies { get; set; }
        public virtual DbSet<DbSyndicate> Syndicates { get; set; }
        public virtual DbSet<DbSyndicateMember> SyndicateMembers { get; set; }
        public virtual DbSet<DbSyndicateRelation> SyndicateRelations { get; set; }
        public virtual DbSet<DbFamily> Families { get; set; }
        public virtual DbSet<DbFamilyMember> FamilyMembers { get; set; }
        public virtual DbSet<DbFamilyRelation> FamilyRelations { get; set; }
        public virtual DbSet<DbArena> Arena { get; set; }
        public virtual DbSet<DbNobility> Nobility { get; set; }

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
            builder.Entity<DbFriend>(e => e.HasKey(x => new { x.CharacterID, x.FriendID }));
            builder.Entity<DbEnemy>(e => e.HasKey(x => new { x.CharacterID, x.EnemyID }));
            builder.Entity<DbTradePartner>(e => e.HasKey(x => new { x.CharacterID, x.PartnerID }));
            builder.Entity<DbMagic>(e => e.HasKey(x => new { x.CharacterID, x.Type }));
            builder.Entity<DbProficiency>(e => e.HasKey(x => new { x.CharacterID, x.Type }));
            builder.Entity<DbSyndicate>(e => e.HasKey(x => x.SyndicateID));
            builder.Entity<DbSyndicateMember>(e => e.HasKey(x => x.CharacterID));
            builder.Entity<DbSyndicateRelation>(e => e.HasKey(x => new { x.SyndicateID, x.TargetID }));
            builder.Entity<DbFamily>(e => e.HasKey(x => x.FamilyID));
            builder.Entity<DbFamilyMember>(e => e.HasKey(x => x.CharacterID));
            builder.Entity<DbFamilyRelation>(e => e.HasKey(x => new { x.FamilyID, x.TargetID }));
            builder.Entity<DbArena>(e => e.HasKey(x => x.CharacterID));
            builder.Entity<DbNobility>(e => e.HasKey(x => x.CharacterID));
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

namespace Comet.Web.Audit;

using Microsoft.EntityFrameworkCore;

// Tables of the game schema that only the web uses
public class WebDbContext(DbContextOptions<WebDbContext> options) : DbContext(options)
{
    public DbSet<DbWebAudit> Audit { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DbWebAudit>(e => e.HasKey(x => x.ID));
    }
}

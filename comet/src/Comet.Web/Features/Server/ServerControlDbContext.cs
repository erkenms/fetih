namespace Comet.Web.Features.Server;

using Comet.Game.Database.Models;
using Microsoft.EntityFrameworkCore;

public class ServerControlDbContext(DbContextOptions<ServerControlDbContext> options) : DbContext(options)
{
    public DbSet<DbServerSettings> Settings => this.Set<DbServerSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DbServerSettings>(e => e.HasKey(x => x.ID));
    }
}

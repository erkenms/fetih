namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class GuildRewardsRepository
    {
        public static async Task<(List<DbGuildRewardPool>, List<DbGuildRewardShare>)> LoadAsync()
        {
            using var db = new ServerDbContext();
            return (await db.GuildRewardPools.ToListAsync(), await db.GuildRewardShares.ToListAsync());
        }

        // The guild's funds and its pool change together
        public static async Task SaveAsync(DbSyndicate guild, DbGuildRewardPool pool)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            db.Syndicates.Update(guild);
            if (await db.GuildRewardPools.AnyAsync(x => x.SyndicateID == pool.SyndicateID && x.Event == pool.Event))
                db.GuildRewardPools.Update(pool);
            else
                db.GuildRewardPools.Add(pool);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // A split empties the pool, gives back what didn't divide, and replaces the last split
        public static async Task SaveSplitAsync(DbSyndicate guild, byte ev, IEnumerable<DbGuildRewardShare> shares)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            db.Syndicates.Update(guild);
            await db.GuildRewardPools.Where(x => x.SyndicateID == guild.SyndicateID && x.Event == ev).ExecuteDeleteAsync();
            await db.GuildRewardShares.Where(x => x.SyndicateID == guild.SyndicateID && x.Event == ev).ExecuteDeleteAsync();
            db.GuildRewardShares.AddRange(shares);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }
}

namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class TeamPkRepository
    {
        public static async Task<List<DbTeamPkReward>> LoadAsync(byte ev)
        {
            using var db = new ServerDbContext();
            return await db.TeamPkRewards.Where(x => x.Event == ev).ToListAsync();
        }

        // A group's new placements replace its old ones, claimed or not
        public static async Task ReplaceGroupAsync(byte ev, byte group, IEnumerable<DbTeamPkReward> rows)
        {
            using var db = new ServerDbContext();
            await db.TeamPkRewards.Where(x => x.Event == ev && x.Group == group).ExecuteDeleteAsync();
            foreach (var row in rows)
            {
                await db.TeamPkRewards.Where(x => x.Event == ev && x.LeaderID == row.LeaderID).ExecuteDeleteAsync();
                db.TeamPkRewards.Add(row);
            }
            await db.SaveChangesAsync();
        }

        public static async Task ClearGroupAsync(byte ev, byte group)
        {
            using var db = new ServerDbContext();
            await db.TeamPkRewards.Where(x => x.Event == ev && x.Group == group).ExecuteDeleteAsync();
        }

        public static async Task SetClaimedAsync(byte ev, uint leaderID, bool claimed)
        {
            using var db = new ServerDbContext();
            await db.TeamPkRewards.Where(x => x.Event == ev && x.LeaderID == leaderID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Claimed, claimed));
        }
    }
}

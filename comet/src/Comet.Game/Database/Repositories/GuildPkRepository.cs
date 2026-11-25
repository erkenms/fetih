namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class GuildPkRepository
    {
        public static async Task<(List<DbGuildPkEntry>, List<DbGuildPkPrize>, List<DbGuildPkExploit>)> LoadAsync()
        {
            using var db = new ServerDbContext();
            return (await db.GuildPkEntries.ToListAsync(), await db.GuildPkPrizes.ToListAsync(),
                await db.GuildPkExploits.ToListAsync());
        }

        // A new contest's qualifiers replace last week's
        public static async Task ReplaceEntriesAsync(IEnumerable<DbGuildPkEntry> rows)
        {
            using var db = new ServerDbContext();
            await db.GuildPkEntries.ExecuteDeleteAsync();
            db.GuildPkEntries.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public static async Task SaveEntriesAsync(IEnumerable<DbGuildPkEntry> rows)
        {
            using var db = new ServerDbContext();
            db.GuildPkEntries.UpdateRange(rows);
            await db.SaveChangesAsync();
        }

        // New prizes of a kind replace the old ones, claimed or not
        public static async Task ReplacePrizesAsync(byte kind, IEnumerable<DbGuildPkPrize> rows)
        {
            using var db = new ServerDbContext();
            await db.GuildPkPrizes.Where(x => x.Kind == kind).ExecuteDeleteAsync();
            db.GuildPkPrizes.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public static async Task SetClaimedAsync(uint guildID, byte kind)
        {
            using var db = new ServerDbContext();
            await db.GuildPkPrizes.Where(x => x.SyndicateID == guildID && x.Kind == kind)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Claimed, true));
        }

        public static async Task SaveExploitsAsync(IEnumerable<DbGuildPkExploit> rows)
        {
            using var db = new ServerDbContext();
            foreach (var row in rows)
            {
                if (await db.GuildPkExploits.AnyAsync(x => x.CharacterID == row.CharacterID))
                    db.GuildPkExploits.Update(row);
                else
                    db.GuildPkExploits.Add(row);
            }
            await db.SaveChangesAsync();
        }
    }
}

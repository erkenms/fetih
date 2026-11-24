namespace Comet.Game.Database.Repositories
{
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class GuildWarRepository
    {
        public const uint RowID = 1;

        public static async Task<DbGuildWar> LoadAsync()
        {
            using var db = new ServerDbContext();
            return await db.GuildWar.FirstOrDefaultAsync(x => x.ID == RowID);
        }

        public static async Task SaveAsync(DbGuildWar state)
        {
            state.ID = RowID;
            using var db = new ServerDbContext();
            if (await db.GuildWar.AnyAsync(x => x.ID == RowID))
                db.GuildWar.Update(state);
            else
                db.GuildWar.Add(state);
            await db.SaveChangesAsync();
        }
    }
}

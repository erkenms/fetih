namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class PortalsRepository
    {
        public static async Task<List<DbPortal>> LoadAllAsync()
        {
            using var db = new ServerDbContext();
            return await db.Portals.AsNoTracking().ToListAsync();
        }
    }
}

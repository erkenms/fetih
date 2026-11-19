namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class NpcsRepository
    {
        public static async Task<List<DbNpc>> LoadAllAsync()
        {
            using var db = new ServerDbContext();
            return await db.Npcs.ToListAsync();
        }
    }
}

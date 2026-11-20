namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class NobilityRepository
    {
        public sealed record Row(uint CharacterID, ulong Donation, string Name, uint Mesh);

        public static async Task<List<Row>> LoadAsync()
        {
            using var db = new ServerDbContext();
            var rows = await db.Nobility
                .Join(db.Characters, n => n.CharacterID, c => c.CharacterID,
                    (n, c) => new { n.CharacterID, n.Donation, c.Name, c.Mesh, c.Avatar })
                .ToListAsync();
            return rows.Select(x => new Row(x.CharacterID, x.Donation, x.Name, x.Mesh + x.Avatar * 10000u)).ToList();
        }

        public static async Task SaveAsync(uint characterID, ulong donation)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO nobility (CharacterID, Donation) VALUES ({characterID}, {donation})
                ON DUPLICATE KEY UPDATE Donation = VALUES(Donation)");
        }
    }
}

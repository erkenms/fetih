namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ArenaRepository
    {
        public sealed record Row(DbArena Record, string Name, byte Level, byte Class, uint Mesh);

        public static async Task<List<Row>> LoadAsync()
        {
            using var db = new ServerDbContext();
            var rows = await db.Arena
                .Join(db.Characters, a => a.CharacterID, c => c.CharacterID,
                    (a, c) => new { a, c.Name, c.Level, c.CurrentClass, c.Mesh, c.Avatar })
                .ToListAsync();
            return rows.Select(x => new Row(x.a, x.Name, x.Level, x.CurrentClass, x.Mesh + x.Avatar * 10000u)).ToList();
        }

        public static async Task SaveAsync(DbArena record)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO arena (CharacterID, Points, Day, WinsToday, LossesToday, WinsTotal, LossesTotal, HonorPoints, HonorTotal)
                VALUES ({record.CharacterID}, {record.Points}, {record.Day}, {record.WinsToday}, {record.LossesToday},
                    {record.WinsTotal}, {record.LossesTotal}, {record.HonorPoints}, {record.HonorTotal})
                ON DUPLICATE KEY UPDATE Points = VALUES(Points), Day = VALUES(Day), WinsToday = VALUES(WinsToday),
                    LossesToday = VALUES(LossesToday), WinsTotal = VALUES(WinsTotal), LossesTotal = VALUES(LossesTotal),
                    HonorPoints = VALUES(HonorPoints), HonorTotal = VALUES(HonorTotal)");
        }
    }
}

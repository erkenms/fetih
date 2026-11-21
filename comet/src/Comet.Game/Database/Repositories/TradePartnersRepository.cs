namespace Comet.Game.Database.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class TradePartnersRepository
    {
        public static async Task<List<(uint ID, string Name, DateTime Since)>> LoadAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            var partners = await db.TradePartners.Where(p => p.CharacterID == characterID)
                .Join(db.Characters, p => p.PartnerID, c => c.CharacterID, (p, c) => new { c.CharacterID, c.Name, p.Since })
                .ToListAsync();
            return partners.Select(p => (p.CharacterID, p.Name, p.Since)).ToList();
        }

        public static async Task AddAsync(uint characterID, uint partnerID, DateTime since)
        {
            using var db = new ServerDbContext();
            db.TradePartners.Add(new DbTradePartner { CharacterID = characterID, PartnerID = partnerID, Since = since });
            db.TradePartners.Add(new DbTradePartner { CharacterID = partnerID, PartnerID = characterID, Since = since });
            await db.SaveChangesAsync();
        }

        public static async Task RemoveAsync(uint characterID, uint partnerID)
        {
            using var db = new ServerDbContext();
            await db.TradePartners.Where(p =>
                p.CharacterID == characterID && p.PartnerID == partnerID ||
                p.CharacterID == partnerID && p.PartnerID == characterID).ExecuteDeleteAsync();
        }
    }
}

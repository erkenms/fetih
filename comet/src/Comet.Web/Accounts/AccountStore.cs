namespace Comet.Web.Accounts;

using Comet.Account.Database;
using Comet.Account.Database.Models;
using Microsoft.EntityFrameworkCore;

// The account schema through the account server's own models
public sealed class AccountStore
{
    public async Task<DbAccount> FindAsync(uint accountId)
    {
        using var db = new ServerDbContext();
        return await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.AccountID == accountId);
    }

    public async Task<DbAccount> FindAsync(string username)
    {
        username = username?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(username))
            return null;
        using var db = new ServerDbContext();
        return await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Username == username);
    }

    // The same accounts the account server turns away
    public static string LoginRefusal(DbAccount account) => (AccountStatus)account.StatusID switch
    {
        AccountStatus.Banned => "banned",
        AccountStatus.Locked => "locked",
        _ => null,
    };
}

namespace Comet.Account.Database.Repositories
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Account.Database.Models;
    using Comet.Network.Security;
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// Repository for defining data access layer (DAL) logic for the account table. Allows
    /// the server to fetch account details for player authentication. Accounts are fetched
    /// on demand for each player authentication request.
    /// </summary>
    public static class AccountsRepository
    {
        /// <summary>
        /// Fetches an account record from the database using the player's username as a
        /// unique key for selecting a single record.
        /// </summary>
        /// <param name="username">Username to pull account info for</param>
        /// <returns>Returns account details from the database.</returns>
        public static async Task<DbAccount> FindAsync(string username)
        {
            using var db = new ServerDbContext();
            return await db.Accounts.Include(x => x.Authority)
                .Include(x => x.Status)
                .Where(x => x.Username == username)
                .SingleOrDefaultAsync();
        }

        /// <summary>
        /// Starts an SRP6 login for the account, or returns null if the account has no
        /// password set (no SRP6 salt and verifier).
        /// </summary>
        /// <param name="account">Account logging in</param>
        public static Srp6 BeginLogin(DbAccount account)
        {
            if (string.IsNullOrEmpty(account.SrpSalt) || string.IsNullOrEmpty(account.SrpVerifier))
                return null;
            return new Srp6(
                Convert.FromHexString(account.SrpSalt),
                Convert.FromHexString(account.SrpVerifier));
        }
    } 
}
